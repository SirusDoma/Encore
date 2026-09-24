using System.Net;
using System.Net.Sockets;
using Encore.Messaging;
using Encore.Server;
using Encore.Sessions;
using Microsoft.Extensions.Options;

namespace Encore.Tests.Server;

public class TcpServerTests
{
    private sealed class RecordingFactory : ISessionFactory<TestTcpSession>
    {
        public List<TcpClient> Clients { get; } = [];

        public Exception? Failure { get; init; }

        public TestTcpSession CreateSession(TcpClient client, params object[] parameters)
        {
            Clients.Add(client);
            if (Failure != null)
                throw Failure;

            return new TestTcpSession();
        }
    }

    private static IOptions<TcpOptions> LocalOptions() =>
        Microsoft.Extensions.Options.Options.Create(new TcpOptions { Address = "127.0.0.1", Port = 0 });

    private static int PortOf<TSession>(ITcpServer<TSession> server) where TSession : ITcpSession =>
        ((IPEndPoint)server.Socket.LocalEndPoint!).Port;

    private static async Task<TcpClient> ConnectAsync<TSession>(ITcpServer<TSession> server) where TSession : ITcpSession
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, PortOf(server)).Within();

        return client;
    }

    [Fact]
    public void Options_AreExposed()
    {
        var options = LocalOptions();
        using var server = new TcpServer<TestTcpSession>(new RecordingFactory(), options);

        Assert.Same(options.Value, server.Options);
        Assert.False(server.Active);
    }

    [Fact]
    public async Task AcceptSession_BeforeStart_Throws()
    {
        using var server = new TcpServer<TestTcpSession>(new RecordingFactory(), LocalOptions());

        await Assert.ThrowsAsync<InvalidOperationException>(() => server.AcceptSession(default));
    }

    [Fact]
    public async Task AcceptSession_CreatesSessionFromFactory()
    {
        var factory = new RecordingFactory();
        using var server = new TcpServer<TestTcpSession>(factory, LocalOptions());
        server.Start();
        server.Start();

        var accept = server.AcceptSession(default);
        using var client = await ConnectAsync(server);
        var session = await accept.Within();

        Assert.True(server.Active);
        Assert.NotNull(session);
        Assert.True(Assert.Single(factory.Clients).Connected);
    }

    [Fact]
    public async Task AcceptSession_FactoryFailure_DisposesClientAndPropagates()
    {
        var factory = new RecordingFactory { Failure = new InvalidDataException() };
        using var server = new TcpServer<TestTcpSession>(factory, LocalOptions());
        server.Start();

        var accept = server.AcceptSession(default);
        using var client = await ConnectAsync(server);

        await Assert.ThrowsAsync<InvalidDataException>(() => accept.Within());
        Assert.Equal(0, await client.ReadAnyAsync());
    }

    [Fact]
    public async Task Stop_CancelsPendingAccept()
    {
        using var server = new TcpServer<TestTcpSession>(new RecordingFactory(), LocalOptions());
        server.Start();

        var accept = server.AcceptSession(default);
        await server.Stop();
        await server.Stop();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => accept.Within());
        Assert.False(server.Active);
    }

    [Fact]
    public async Task AcceptSession_Cancelled_Throws()
    {
        using var server = new TcpServer<TestTcpSession>(new RecordingFactory(), LocalOptions());
        using var cts    = new CancellationTokenSource();
        server.Start();

        var accept = server.AcceptSession(cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => accept.Within());
        Assert.True(server.Active);
    }

    [Fact]
    public async Task Start_AfterStop_AcceptsAgain()
    {
        var factory = new RecordingFactory();
        using var server = new TcpServer<TestTcpSession>(factory, LocalOptions());
        server.Start();
        await server.Stop();
        server.Start();

        var accept = server.AcceptSession(default);
        using var client = await ConnectAsync(server);

        Assert.NotNull(await accept.Within());
    }

    [Fact]
    public async Task Dispose_StopsServer()
    {
        var server = new TcpServer<TestTcpSession>(new RecordingFactory(), LocalOptions());
        server.Start();
        var accept = server.AcceptSession(default);

        server.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => accept.Within());
        Assert.False(server.Active);
    }

    [Fact]
    public async Task DefaultServer_CreatesTcpSessions()
    {
        using ITcpServer server = new TcpServer(LocalOptions());
        server.Start();

        var accept = server.AcceptSession(default);
        using var client  = await ConnectAsync(server);
        using var session = await accept.Within();

        Assert.IsType<TcpSession>(session);
        Assert.Same(server.Options, session.Options);
    }

    [Fact]
    public void TcpOptions_Defaults()
    {
        var options = new TcpOptions();

        Assert.Equal("Server", TcpOptions.Section);
        Assert.Equal("127.0.0.1", options.Address);
        Assert.Equal(0, options.Port);
        Assert.Equal((int)SocketOptionName.MaxConnections, options.MaxConnections);
        Assert.Equal(4096, options.PacketBufferSize);
    }

    [Fact]
    public async Task SessionFactory_UsesProvidedFramerFactory()
    {
        using var pair  = await Loopback.CreateAsync();
        var options     = LocalOptions();
        var framers     = new List<NetworkStream>();
        var factory     = new SessionFactory(options, new DelegateFramerFactory(stream =>
        {
            framers.Add(stream);
            return new SizePrefixedMessageFramer<byte>(stream);
        }));

        using var session = factory.CreateSession(pair.Local);
        await session.WriteFrame([7], default).Within();

        Assert.Single(framers);
        Assert.Same(options.Value, session.Options);
        Assert.Equal(Convert.FromHexString("0207"), await pair.Remote.ReadBytesAsync(2));
    }

    [Fact]
    public async Task SessionFactory_DefaultsToUShortFramer()
    {
        using var pair    = await Loopback.CreateAsync();
        using var session = new SessionFactory(LocalOptions()).CreateSession(pair.Local);

        await session.WriteFrame([7], default).Within();

        Assert.Equal(Convert.FromHexString("030007"), await pair.Remote.ReadBytesAsync(3));
    }

    private sealed class DelegateFramerFactory(Func<NetworkStream, IMessageFramer> create) : IMessageFramerFactory
    {
        public IMessageFramer CreateFramer(NetworkStream stream) => create(stream);
    }
}
