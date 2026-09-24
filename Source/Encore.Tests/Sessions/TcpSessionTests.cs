using System.Net.Sockets;
using Encore.Messaging;
using Encore.Server;
using Encore.Sessions;

namespace Encore.Tests.Sessions;

public class TcpSessionTests
{
    private sealed class ProbeSession(TcpClient client, TcpOptions options, ICommandDispatcher dispatcher)
        : TcpSession(client, options, new SizePrefixedMessageFramerFactory<ushort>(), dispatcher)
    {
        public int DisconnectedCallbacks { get; private set; }

        protected override void OnDisconnected() => DisconnectedCallbacks++;

        public void Trigger() => TriggerDisconnectEvent();
    }

    private static TcpSession Create(Loopback pair, ICommandDispatcher? dispatcher = null, TcpOptions? options = null) =>
        new(pair.Local, options ?? new TcpOptions(), new SizePrefixedMessageFramerFactory<ushort>(),
            dispatcher ?? new CommandDispatcher());

    [Fact]
    public async Task Execute_DispatchesFramesAndWritesResponses()
    {
        using var pair = await Loopback.CreateAsync();
        ICommandDispatcher dispatcher = new CommandDispatcher();
        dispatcher.Map<TcpSession, PingRequest, PingResponse>((_, request) =>
            Task.FromResult(new PingResponse { Value = request.Value + 1 }));

        using var session = Create(pair, dispatcher);
        var remote = new SizePrefixedMessageFramer<ushort>(pair.Remote.GetStream());

        var execution = session.Execute(default);
        Assert.True(session.Connected);

        await remote.WriteFrame(Wire.Encode(new PingRequest { Value = 1 }), default).Within();
        await remote.WriteFrame(Wire.Encode(new PingRequest { Value = 10 }), default).Within();

        Assert.Equal(2, Wire.Decode<PingResponse>(await remote.ReadFrame().Within()).Value);
        Assert.Equal(11, Wire.Decode<PingResponse>(await remote.ReadFrame().Within()).Value);
        Assert.False(execution.IsCompleted);
    }

    [Fact]
    public async Task Execute_IgnoresEmptyFrames()
    {
        using var pair = await Loopback.CreateAsync();
        ICommandDispatcher dispatcher = new CommandDispatcher();
        var received = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Map<TcpSession, PingRequest>((_, request) =>
        {
            received.TrySetResult(request.Value);
            return Task.CompletedTask;
        });

        using var session = Create(pair, dispatcher);
        var execution = session.Execute(default);

        await pair.Remote.WriteBytesAsync(Convert.FromHexString("0200"));
        await new SizePrefixedMessageFramer<ushort>(pair.Remote.GetStream())
            .WriteFrame(Wire.Encode(new PingRequest { Value = 3 }), default).Within();

        Assert.Equal(3, await received.Task.Within());
        Assert.False(execution.IsCompleted);
    }

    [Fact]
    public async Task Execute_PeerDisconnect_RaisesDisconnectedAndFaults()
    {
        using var pair = await Loopback.CreateAsync();
        using var session = Create(pair);
        var disconnected = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Disconnected += (sender, _) => disconnected.TrySetResult(sender);

        var execution = session.Execute(default);
        pair.Remote.Close();

        await Assert.ThrowsAnyAsync<IOException>(() => execution.Within());
        Assert.Same(session, await disconnected.Task.Within());
        Assert.False(session.Connected);
    }

    [Fact]
    public async Task Execute_Cancelled_RaisesDisconnected()
    {
        using var pair = await Loopback.CreateAsync();
        using var session = Create(pair);
        using var cts = new CancellationTokenSource();
        var disconnected = false;
        session.Disconnected += (_, _) => disconnected = true;

        var execution = session.Execute(cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution.Within());
        Assert.True(disconnected);
        Assert.False(session.Connected);
    }

    [Fact]
    public async Task Execute_DispatchFailure_FaultsAndDisconnects()
    {
        using var pair = await Loopback.CreateAsync();
        using var session = Create(pair);
        var disconnected = false;
        session.Disconnected += (_, _) => disconnected = true;

        var execution = session.Execute(default);
        await pair.Remote.WriteBytesAsync(Convert.FromHexString("04009999"));

        await Assert.ThrowsAsync<NotSupportedException>(() => execution.Within());
        Assert.True(disconnected);
    }

    [Fact]
    public async Task DisconnectedHandlers_AreClearedAfterFiring()
    {
        using var pair = await Loopback.CreateAsync();
        using var session = new ProbeSession(pair.Local, new TcpOptions(), new CommandDispatcher());
        var count = 0;
        session.Disconnected += (_, _) => count++;

        session.Trigger();
        session.Trigger();

        Assert.Equal(1, count);
        Assert.Equal(2, session.DisconnectedCallbacks);
    }

    [Fact]
    public async Task WriteAndReadFrame_UseFramer()
    {
        using var pair = await Loopback.CreateAsync();
        using var session = Create(pair);

        await session.WriteFrame([1, 2], default).Within();
        Assert.Equal(Convert.FromHexString("04000102"), await pair.Remote.ReadBytesAsync(4));

        await pair.Remote.WriteBytesAsync(Convert.FromHexString("030009"));
        Assert.Equal(new byte[] { 9 }, await session.ReadFrame(default).Within());
    }

    [Fact]
    public async Task ReadFrame_UsesPacketBufferSizeOption()
    {
        using var pair = await Loopback.CreateAsync();
        using var session = Create(pair, options: new TcpOptions { PacketBufferSize = 0 });

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.ReadFrame(default).Within());
    }

    [Fact]
    public async Task Terminate_ClosesConnection()
    {
        using var pair = await Loopback.CreateAsync();
        using var session = Create(pair);

        session.Terminate();

        Assert.Equal(0, await pair.Remote.ReadAnyAsync());
    }

    [Fact]
    public async Task Dispose_ClosesConnection()
    {
        using var pair = await Loopback.CreateAsync();
        var session = Create(pair);

        session.Dispose();

        Assert.Equal(0, await pair.Remote.ReadAnyAsync());
    }

    [Fact]
    public async Task Authorization_StoresTypedToken()
    {
        using var pair = await Loopback.CreateAsync();
        using var session = Create(pair);

        Assert.False(session.Authorized);
        Assert.Throws<InvalidOperationException>(() => session.GetAuthorizedToken());

        session.Authorize("token");

        Assert.True(session.Authorized);
        Assert.Equal("token", session.GetAuthorizedToken());
        Assert.Equal("token", session.GetAuthorizedToken<string>());
        Assert.Throws<InvalidOperationException>(() => session.GetAuthorizedToken<int>());

        session.Authorize<string?>(null);
        Assert.False(session.Authorized);
    }

    [Fact]
    public async Task ExposesSocketOptionsAndProperties()
    {
        using var pair = await Loopback.CreateAsync();
        var options = new TcpOptions();
        using var session = Create(pair, options: options);

        session.Properties["key"] = 1;

        Assert.Same(pair.Local.Client, session.Socket);
        Assert.Same(options, session.Options);
        Assert.Same(session.Properties, ((ISession)session).Properties);
        Assert.Equal(1, ((ISession)session).Properties["key"]);
        Assert.False(session.Connected);
    }
}
