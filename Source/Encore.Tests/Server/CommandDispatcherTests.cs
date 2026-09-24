using Encore.Messaging;
using Encore.Server;
using Encore.Sessions;

namespace Encore.Tests.Server;

public class CommandDispatcherTests
{
    [Fact]
    public async Task Dispatch_Payload_InvokesRequestHandler()
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();
        var session = new TestSession();
        PingRequest? received = null;
        TestSession? receivedSession = null;

        dispatcher.Map<TestSession, PingRequest>((s, request) =>
        {
            receivedSession = s;
            received        = request;
            return Task.CompletedTask;
        });

        await dispatcher.Dispatch(session, Wire.Encode(new PingRequest { Value = 7 }), default);

        Assert.Same(session, receivedSession);
        Assert.Equal(7, received?.Value);
        Assert.Empty(session.Frames);
    }

    [Fact]
    public async Task Dispatch_Payload_WritesEncodedResponse()
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();
        var session = new TestSession();

        dispatcher.Map<ISession, PingRequest, PingResponse>((_, request) =>
            Task.FromResult(new PingResponse { Value = request.Value + 1 }));

        await dispatcher.Dispatch(session, Wire.Encode(new PingRequest { Value = 7 }), default);

        Assert.Equal(8, Wire.Decode<PingResponse>(Assert.Single(session.Frames)).Value);
    }

    [Fact]
    public async Task Dispatch_PassesCancellationTokenToHandlers()
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();
        using var cts = new CancellationTokenSource();
        var tokens = new List<CancellationToken>();

        dispatcher.Map<ISession, PingRequest, PingResponse>((_, _, token) =>
        {
            tokens.Add(token);
            return Task.FromResult(new PingResponse());
        });
        dispatcher.Map<ISession, Cmd>(Cmd.NotifyRequest, (_, token) =>
        {
            tokens.Add(token);
            return Task.CompletedTask;
        });

        await dispatcher.Dispatch(new TestSession(), new PingRequest(), cts.Token);
        await dispatcher.Dispatch(new TestSession(), Cmd.NotifyRequest, cts.Token);

        Assert.Equal([cts.Token, cts.Token], tokens);
    }

    [Fact]
    public async Task Dispatch_CommandOnlyPayload_InvokesCommandHandler()
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();
        var session = new TestSession();

        dispatcher.Map<TestSession, Cmd>(Cmd.LogoutRequest, s =>
        {
            s.Record("logout");
            return Task.CompletedTask;
        });

        await dispatcher.Dispatch(session, Convert.FromHexString("3000"), default);
        await dispatcher.Dispatch(session, Cmd.LogoutRequest, default);

        Assert.Equal(["logout", "logout"], session.Log);
        Assert.Empty(session.Frames);
    }

    [Fact]
    public async Task Dispatch_Message_PassesSameInstance()
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();
        var request = new PingRequest { Value = 1 };
        PingRequest? received = null;

        dispatcher.Map<ISession, PingRequest>((_, r) =>
        {
            received = r;
            return Task.CompletedTask;
        });

        await dispatcher.Dispatch(new TestSession(), request, default);

        Assert.Same(request, received);
    }

    [Fact]
    public async Task Dispatch_MultipleHandlers_RunInRegistrationOrder()
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();
        var session = new TestSession();

        dispatcher.Map<TestSession, Cmd>(Cmd.PingRequest, s =>
        {
            s.Record("command");
            return Task.CompletedTask;
        });
        dispatcher.Map<TestSession, PingRequest, PingResponse>((s, r) =>
        {
            s.Record("first");
            return Task.FromResult(new PingResponse { Value = 1 });
        });
        dispatcher.Map<TestSession, PingRequest, PingResponse>((s, r) =>
        {
            s.Record("second");
            return Task.FromResult(new PingResponse { Value = 2 });
        });

        await dispatcher.Dispatch(session, Wire.Encode(new PingRequest()), default);

        Assert.Equal(["command", "first", "second"], session.Log);
        Assert.Equal([1, 2], session.Frames.Select(f => Wire.Decode<PingResponse>(f).Value));
    }

    [Fact]
    public void Map_ConflictingRequestTypeForSameCommand_Throws()
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();
        dispatcher.Map<ISession, PingRequest>((_, _) => Task.CompletedTask);

        Assert.Throws<InvalidOperationException>(() =>
            dispatcher.Map<ISession, AltPingRequest>((_, _) => Task.CompletedTask));
    }

    [Fact]
    public async Task Dispatch_UnknownPayload_Throws()
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            dispatcher.Dispatch(new TestSession(), Convert.FromHexString("9999"), default));
    }

    [Fact]
    public async Task Dispatch_UnmappedCommand_Throws()
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            dispatcher.Dispatch(new TestSession(), Cmd.UnmappedRequest, default));
    }

    [Fact]
    public async Task Dispatch_MalformedPayload_Throws()
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();

        await Assert.ThrowsAsync<FormatException>(() =>
            dispatcher.Dispatch(new TestSession(), [0x01], default));
    }

    [Fact]
    public async Task Dispatch_CommandDecodingFailure_GoesThroughExceptionFilters()
    {
        var codec = new TrackingCodec { DecodeCommandOverride = _ => throw new InvalidDataException() };
        ICommandDispatcher dispatcher = new CommandDispatcher(codec);
        CommandExceptionHandlerContext? captured = null;

        dispatcher.Map<ISession, PingRequest>((_, _) => Task.CompletedTask);
        dispatcher.AddExceptionFilter(new DelegateExceptionHandler(context =>
        {
            captured        = context;
            context.Handled = true;
        }));

        await dispatcher.Dispatch(new TestSession(), Wire.Encode(new PingRequest { Value = 3 }), default);

        Assert.IsType<InvalidDataException>(captured?.Exception);
        Assert.Null(captured!.Descriptor);
        Assert.Equal(3, Assert.IsType<PingRequest>(captured.Request).Value);
    }

    [Fact]
    public async Task Dispatch_UsesProvidedCodec()
    {
        var codec = new TrackingCodec();
        ICommandDispatcher dispatcher = new CommandDispatcher(codec);

        dispatcher.Map<ISession, PingRequest>((_, _) => Task.CompletedTask);
        await dispatcher.Dispatch(new TestSession(), Wire.Encode(new PingRequest()), default);

        Assert.Equal(1, codec.DecodeCount);
        Assert.Equal(typeof(PingRequest), codec.GetRegisteredType(typeof(PingRequest)));
    }

    [Fact]
    public async Task Dispatch_UsesCodecCommandWidth()
    {
        var codec = new DefaultMessageCodec<uint>();
        ICommandDispatcher dispatcher = new CommandDispatcher(codec);
        var session = new TestSession();

        dispatcher.Map<ISession, WideRequest, PingResponse>((_, request) =>
            Task.FromResult(new PingResponse { Value = request.Value + 1 }));

        await dispatcher.Dispatch(session, codec.Encode(new WideRequest { Value = 7 }), default);

        Assert.Equal(Convert.FromHexString("0200000008000000"), Assert.Single(session.Frames));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            dispatcher.Dispatch(session, WideCmd.UnmappedRequest, default));
    }

    [Fact]
    public async Task Dispatch_UnmappedCommand_FormatsCodeByEnumWidth()
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();

        var narrow = await Assert.ThrowsAsync<NotSupportedException>(() =>
            dispatcher.Dispatch(new TestSession(), Cmd.UnmappedRequest, default));
        var wide = await Assert.ThrowsAsync<NotSupportedException>(() =>
            dispatcher.Dispatch(new TestSession(), WideCmd.UnmappedRequest, default));
        var signed = await Assert.ThrowsAsync<NotSupportedException>(() =>
            dispatcher.Dispatch(new TestSession(), SignedCmd.UnmappedRequest, default));

        Assert.Equal("Command '0x0050' is not recognized", narrow.Message);
        Assert.Equal("Command '0x00010050' is not recognized", wide.Message);
        Assert.Equal("Command '0xFFFFFFFF' is not recognized", signed.Message);
    }

    [Fact]
    public async Task Dispatch_HandlerException_PropagatesAndStopsRemainingHandlers()
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();
        var session = new TestSession();

        dispatcher.Map<TestSession, PingRequest>((_, _) => throw new InvalidDataException());
        dispatcher.Map<TestSession, PingRequest>((s, _) =>
        {
            s.Record("second");
            return Task.CompletedTask;
        });

        await Assert.ThrowsAsync<InvalidDataException>(() => dispatcher.Dispatch(session, new PingRequest(), default));
        Assert.Empty(session.Log);
    }

    [Fact]
    public async Task Dispatch_NullArguments_Throw()
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();

        await Assert.ThrowsAsync<ArgumentNullException>(() => dispatcher.Dispatch(null!, [0, 0], default));
        await Assert.ThrowsAsync<ArgumentNullException>(() => dispatcher.Dispatch(new TestSession(), (byte[])null!, default));
        await Assert.ThrowsAsync<ArgumentNullException>(() => dispatcher.Dispatch<PingRequest>(new TestSession(), null!, default));
        await Assert.ThrowsAsync<ArgumentNullException>(() => dispatcher.Dispatch(new TestSession(), (Enum)null!, default));
    }
}
