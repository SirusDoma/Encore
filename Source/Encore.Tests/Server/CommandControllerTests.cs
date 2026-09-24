using Encore.Server;
using Encore.Sessions;

namespace Encore.Tests.Server;

public class CommandControllerTests
{
    [LogFilter("class-b", Order = 2)]
    [LogFilter("class-a", Order = 1)]
    public sealed class GameController(TestSession session) : CommandController<TestSession>(session)
    {
        public TestSession TypedSession => Session;

        [LogFilter("method")]
        [CommandHandler]
        public Task<PingResponse> Ping(PingRequest request)
        {
            Session.Record("ping");
            return Task.FromResult(new PingResponse { Value = request.Value + 1 });
        }
    }

    public sealed class ShapesController(TestSession session) : CommandController<TestSession>(session)
    {
        [CommandHandler]
        public EchoResponse Echo(EchoRequest request) => new() { Text = request.Text.ToUpperInvariant() };

        [CommandHandler]
        public async Task<PingResponse> Ping(PingRequest request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            Session.Record($"ping:{cancellationToken.CanBeCanceled}");
            return new PingResponse { Value = request.Value * 2 };
        }

        [CommandHandler(Cmd.NotifyRequest)]
        public Task Notify(CancellationToken cancellationToken)
        {
            Session.Record("notify");
            return Task.CompletedTask;
        }

        [CommandHandler(Cmd.LogoutRequest, Cmd.LogoutResponse)]
        public void Logout() => Session.Record("logout");

        [CommandHandler(Cmd.CrashRequest)]
        public Task Crash() => throw new InvalidDataException("sync");

        [CommandHandler(Cmd.CrashAsyncRequest)]
        public async Task CrashAsync()
        {
            await Task.Yield();
            throw new TimeoutException("async");
        }
    }

    public sealed class MultiController(ISession session) : CommandController(session)
    {
        public ISession ExposedSession => Session;

        [CommandHandler(Cmd.NotifyRequest)]
        [CommandHandler(Cmd.LogoutRequest)]
        public Task Handle()
        {
            ((TestSession)Session).Record("multi");
            return Task.CompletedTask;
        }
    }

    public sealed class DuplicateCommandController(ISession session) : CommandController(session)
    {
        [CommandHandler(Cmd.NotifyRequest)]
        [CommandHandler(Cmd.NotifyRequest)]
        public Task Handle() => Task.CompletedTask;
    }

    public sealed class DuplicateDefaultController(ISession session) : CommandController(session)
    {
        [CommandHandler]
        [CommandHandler]
        public Task Handle(PingRequest request) => Task.CompletedTask;
    }

    private static ICommandDispatcher Map<TController>(Func<TestSession, TController> factory, params ICommandFilter[] filters)
        where TController : CommandController
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();
        dispatcher.Map<TestSession, TController>(factory, filters);

        return dispatcher;
    }

    [Fact]
    public async Task AsyncRequestResponse_WritesResponse()
    {
        var dispatcher = Map(s => new GameController(s));
        var session    = new TestSession();

        await dispatcher.Dispatch(session, Wire.Encode(new PingRequest { Value = 1 }), default);

        Assert.Equal(2, Wire.Decode<PingResponse>(Assert.Single(session.Frames)).Value);
    }

    [Fact]
    public async Task SyncRequestResponse_WritesResponse()
    {
        var dispatcher = Map(s => new ShapesController(s));
        var session    = new TestSession();

        await dispatcher.Dispatch(session, new EchoRequest { Text = "hi" }, default);

        Assert.Equal("HI", Wire.Decode<EchoResponse>(Assert.Single(session.Frames)).Text);
    }

    [Fact]
    public async Task CancelableHandler_ReceivesToken()
    {
        var dispatcher = Map(s => new ShapesController(s));
        var session    = new TestSession();
        using var cts  = new CancellationTokenSource();

        await dispatcher.Dispatch(session, new PingRequest { Value = 4 }, cts.Token);

        Assert.Equal(["ping:True"], session.Log);
        Assert.Equal(8, Wire.Decode<PingResponse>(Assert.Single(session.Frames)).Value);
    }

    [Fact]
    public async Task CommandOnlyHandler_IsInvokedWithoutResponse()
    {
        var dispatcher = Map(s => new ShapesController(s));
        var session    = new TestSession();

        await dispatcher.Dispatch(session, Convert.FromHexString("2000"), default);

        Assert.Equal(["notify"], session.Log);
        Assert.Empty(session.Frames);
    }

    [Fact]
    public async Task VoidHandlerWithResponseCommand_WritesResponseCommand()
    {
        var dispatcher = Map(s => new ShapesController(s));
        var session    = new TestSession();

        await dispatcher.Dispatch(session, Cmd.LogoutRequest, default);

        Assert.Equal(["logout"], session.Log);
        Assert.Equal(Convert.FromHexString("3100"), Assert.Single(session.Frames));
    }

    [Fact]
    public async Task HandlerExceptions_AreUnwrapped()
    {
        var dispatcher = Map(s => new ShapesController(s));

        var sync  = await Assert.ThrowsAsync<InvalidDataException>(() =>
            dispatcher.Dispatch(new TestSession(), Cmd.CrashRequest, default));
        var async = await Assert.ThrowsAsync<TimeoutException>(() =>
            dispatcher.Dispatch(new TestSession(), Cmd.CrashAsyncRequest, default));

        Assert.Equal("sync", sync.Message);
        Assert.Equal("async", async.Message);
    }

    [Fact]
    public async Task NewControllerIsCreatedPerDispatch()
    {
        var sessions = new List<TestSession>();
        var dispatcher = Map(s =>
        {
            sessions.Add(s);
            return new ShapesController(s);
        });
        var first  = new TestSession();
        var second = new TestSession();

        await dispatcher.Dispatch(first, Cmd.NotifyRequest, default);
        await dispatcher.Dispatch(second, Cmd.NotifyRequest, default);

        Assert.Equal([first, second], sessions);
    }

    [Fact]
    public async Task Filters_RunGlobalThenMappedThenMethodThenClassByOrder()
    {
        var dispatcher = Map(s => new GameController(s), new DelegateFilter
        {
            Executing = c => ((TestSession)c.Session).Record("mapped:executing"),
            Executed  = c => ((TestSession)c.Session).Record("mapped:executed"),
        });
        var session = new TestSession();

        dispatcher.AddFilter(new DelegateFilter { Executing = c => ((TestSession)c.Session).Record("global:executing") });

        await dispatcher.Dispatch(session, new PingRequest(), default);

        Assert.Equal(
        [
            "global:executing", "mapped:executing", "method:executing", "class-a:executing", "class-b:executing",
            "ping",
            "mapped:executed", "method:executed", "class-a:executed", "class-b:executed",
        ], session.Log);
    }

    [Fact]
    public async Task MultipleAttributes_MapOneMethodToSeveralCommands()
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();
        var session = new TestSession();

        dispatcher.Map<ISession, MultiController>(s => new MultiController(s));

        await dispatcher.Dispatch(session, Cmd.NotifyRequest, default);
        await dispatcher.Dispatch(session, Cmd.LogoutRequest, default);

        Assert.Equal(["multi", "multi"], session.Log);
    }

    [Fact]
    public void DuplicateAttributes_Throw()
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();

        Assert.Throws<InvalidOperationException>(() =>
            dispatcher.Map<ISession, DuplicateCommandController>(s => new DuplicateCommandController(s)));
        Assert.Throws<InvalidOperationException>(() =>
            dispatcher.Map<ISession, DuplicateDefaultController>(s => new DuplicateDefaultController(s)));
    }

    [Fact]
    public async Task ExecutorOverload_WrapsControllerInvocation()
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();
        var session = new TestSession();

        dispatcher.Map<TestSession, ShapesController>(async (s, action) =>
        {
            s.Record("before");
            await action(new ShapesController(s));
            s.Record("after");
        });

        await dispatcher.Dispatch(session, Cmd.NotifyRequest, default);

        Assert.Equal(["before", "notify", "after"], session.Log);
    }

    [Fact]
    public async Task MultipleControllers_ForSameCommand_AllRun()
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();
        var session = new TestSession();

        dispatcher.Map<ISession, MultiController>(s => new MultiController(s));
        dispatcher.Map<TestSession, ShapesController>(s => new ShapesController(s));

        await dispatcher.Dispatch(session, Cmd.NotifyRequest, default);

        Assert.Equal(["multi", "notify"], session.Log);
    }

    [Fact]
    public void Session_IsExposedUntypedAndTyped()
    {
        var session = new TestSession();

        Assert.Same(session, new MultiController(session).ExposedSession);
        Assert.Same(session, new GameController(session).TypedSession);
    }
}
