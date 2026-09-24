using System.Collections;
using System.Reflection;
using Encore.Server;
using Encore.Sessions;

namespace Encore.Tests.Server;

public class ExceptionFilterTests
{
    private sealed class HandledExceptionHandler : CommandExceptionHandler
    {
        public override void Handle(CommandExceptionHandlerContext context)
        {
            ((TestSession)context.Session).Record($"handled:{context.Exception.GetType().Name}");
            context.Handled = true;
        }
    }

    private sealed class SelectiveExceptionHandler : CommandExceptionHandler
    {
        public List<Exception> Handled { get; } = [];

        public override bool ShouldHandle(CommandExceptionContext context) => context.Exception is InvalidDataException;

        public override void Handle(CommandExceptionHandlerContext context)
        {
            Handled.Add(context.Exception);
            context.Handled = true;
        }
    }

    private sealed class PlainExceptionHandler : CommandExceptionHandler;

    private sealed class SessionExceptionLogger : CommandExceptionLogger
    {
        public override void Log(CommandExceptionLoggerContext context) =>
            ((TestSession)context.Session).Record($"logged:{context.Exception.GetType().Name}");
    }

    private sealed class CountingExceptionLogger : CommandExceptionLogger
    {
        public int Count { get; private set; }

        public override void Log(CommandExceptionLoggerContext context) => Count++;
    }

    private sealed class BrokenSession : TestSession, ISession
    {
        Task ISession.WriteFrame(byte[] payload, CancellationToken cancellationToken) =>
            Task.FromException(new IOException("broken"));
    }

    private static ICommandDispatcher CreateFailingDispatcher(Exception? exception = null)
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();
        dispatcher.Map<TestSession, PingRequest, PingResponse>((_, _) => throw exception ?? new InvalidDataException());

        return dispatcher;
    }

    private static CommandExceptionContext Context(Exception exception) => new(exception, new TestSession(), null);

    [Fact]
    public async Task Handler_HandledException_IsSwallowed()
    {
        var dispatcher = CreateFailingDispatcher();
        var session    = new TestSession();

        dispatcher.AddExceptionFilter<HandledExceptionHandler>();

        await dispatcher.Dispatch(session, new PingRequest(), default);

        Assert.Equal(["handled:InvalidDataException"], session.Log);
        Assert.Empty(session.Frames);
    }

    [Fact]
    public async Task Handler_Result_IsWrittenToSession()
    {
        var dispatcher = CreateFailingDispatcher();
        var session    = new TestSession();

        dispatcher.AddExceptionFilter(new DelegateExceptionHandler(c => c.Result = new FailureResponse { Reason = "oops" }));

        await dispatcher.Dispatch(session, new PingRequest(), default);

        Assert.Equal("oops", Wire.Decode<FailureResponse>(Assert.Single(session.Frames)).Reason);
    }

    [Fact]
    public async Task Handler_Unhandled_Rethrows()
    {
        var dispatcher = CreateFailingDispatcher();
        var called     = false;

        dispatcher.AddExceptionFilter(new DelegateExceptionHandler(_ => called = true));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            dispatcher.Dispatch(new TestSession(), new PingRequest(), default));
        Assert.True(called);
    }

    [Fact]
    public async Task Handler_ReceivesFailureContext()
    {
        var dispatcher = CreateFailingDispatcher();
        var session    = new TestSession();
        var request    = new PingRequest();
        CommandExceptionHandlerContext? captured = null;

        dispatcher.AddExceptionFilter(new DelegateExceptionHandler(c =>
        {
            captured  = c;
            c.Handled = true;
        }));

        await dispatcher.Dispatch(session, request, default);

        Assert.IsType<InvalidDataException>(captured!.Exception);
        Assert.Same(session, captured.Session);
        Assert.Same(request, captured.Request);
        Assert.Equal(typeof(PingRequest), captured.Descriptor?.RequestType);
        Assert.Same(captured.Exception, captured.ExceptionContext.Exception);
    }

    [Fact]
    public async Task Handler_HandlesUnknownCommandWithoutDescriptor()
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();
        CommandExceptionHandlerContext? captured = null;

        dispatcher.AddExceptionFilter(new DelegateExceptionHandler(c =>
        {
            captured  = c;
            c.Handled = true;
        }));

        await dispatcher.Dispatch(new TestSession(), Convert.FromHexString("9999"), default);

        Assert.IsType<NotSupportedException>(captured!.Exception);
        Assert.Null(captured.Descriptor);
        Assert.Null(captured.Request);
    }

    [Fact]
    public async Task Handler_HandlesUnmappedCommandWithResult()
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();
        var session = new TestSession();

        dispatcher.AddExceptionFilter(new DelegateExceptionHandler(c => c.Result = new FailureResponse { Reason = "unmapped" }));

        await dispatcher.Dispatch(session, Cmd.UnmappedRequest, default);

        Assert.Equal("unmapped", Wire.Decode<FailureResponse>(Assert.Single(session.Frames)).Reason);
    }

    [Fact]
    public async Task Handler_HandlesMalformedPayloadWithResult()
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();
        var session = new TestSession();

        dispatcher.AddExceptionFilter(new DelegateExceptionHandler(c => c.Result = new FailureResponse { Reason = "bad" }));

        await dispatcher.Dispatch(session, [0x01], default);

        Assert.Equal("bad", Wire.Decode<FailureResponse>(Assert.Single(session.Frames)).Reason);
    }

    [Fact]
    public async Task ResponseWriteFailure_BypassesFiltersAndDoesNotStopRemainingHandlers()
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();
        var session = new BrokenSession();
        var logged  = false;

        dispatcher.Map<TestSession, PingRequest, PingResponse>((_, _) => Task.FromResult(new PingResponse()));
        dispatcher.Map<TestSession, PingRequest>((s, _) =>
        {
            s.Record("second");
            return Task.CompletedTask;
        });
        dispatcher.AddExceptionLogger(new DelegateExceptionLogger(_ => logged = true));
        dispatcher.AddExceptionFilter<HandledExceptionHandler>();

        await Assert.ThrowsAsync<IOException>(() => dispatcher.Dispatch(session, new PingRequest(), default));

        Assert.Equal(["second"], session.Log);
        Assert.False(logged);
    }

    [Fact]
    public void AddExceptionFilter_Twice_Throws()
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();
        dispatcher.AddExceptionFilter<HandledExceptionHandler>();

        Assert.Throws<InvalidOperationException>(() => dispatcher.AddExceptionFilter<HandledExceptionHandler>());
        Assert.Throws<InvalidOperationException>(() => dispatcher.AddExceptionFilter(new HandledExceptionHandler()));
    }

    [Fact]
    public void AddExceptionFilterAndLogger_Null_Throws()
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();

        Assert.Throws<ArgumentNullException>(() => dispatcher.AddExceptionFilter(null!));
        Assert.Throws<ArgumentNullException>(() => dispatcher.AddExceptionLogger(null!));
    }

    [Fact]
    public async Task Loggers_AllRunBeforeHandler()
    {
        var dispatcher = CreateFailingDispatcher();
        var session    = new TestSession();

        dispatcher.AddExceptionLogger<SessionExceptionLogger>();
        dispatcher.AddExceptionLogger(new DelegateExceptionLogger(c => ((TestSession)c.Session).Record("delegate")));
        dispatcher.AddExceptionFilter<HandledExceptionHandler>();

        await dispatcher.Dispatch(session, new PingRequest(), default);

        Assert.Equal(["logged:InvalidDataException", "delegate", "handled:InvalidDataException"], session.Log);
    }

    [Fact]
    public async Task Logger_SuppressingPropagation_SkipsHandlerAndContinuesWithNextHandler()
    {
        var dispatcher = CreateFailingDispatcher();
        var session    = new TestSession();

        dispatcher.Map<TestSession, PingRequest, PingResponse>((s, _) =>
        {
            s.Record("second");
            return Task.FromResult(new PingResponse());
        });
        dispatcher.AddExceptionLogger(new DelegateExceptionLogger(c => c.PropagateException = false));
        dispatcher.AddExceptionLogger(new DelegateExceptionLogger(c => ((TestSession)c.Session).Record("logged")));
        dispatcher.AddExceptionFilter<HandledExceptionHandler>();

        await dispatcher.Dispatch(session, new PingRequest(), default);

        Assert.Equal(["logged", "second"], session.Log);
        Assert.Single(session.Frames);
    }

    [Fact]
    public async Task Logger_WithoutHandler_ExceptionPropagates()
    {
        var dispatcher = CreateFailingDispatcher();
        var session    = new TestSession();

        dispatcher.AddExceptionLogger<SessionExceptionLogger>();

        await Assert.ThrowsAsync<InvalidDataException>(() => dispatcher.Dispatch(session, new PingRequest(), default));
        Assert.Equal(["logged:InvalidDataException"], session.Log);
    }

    [Fact]
    public async Task CommandExceptionHandler_RespectsShouldHandle()
    {
        var handler = new SelectiveExceptionHandler();
        var skipped = new CommandExceptionHandlerContext(Context(new IOException()));
        var handled = new CommandExceptionHandlerContext(Context(new InvalidDataException()));

        await ((ICommandExceptionHandler)handler).HandleAsync(skipped, default);
        await ((ICommandExceptionHandler)handler).HandleAsync(handled, default);

        Assert.False(skipped.Handled);
        Assert.True(handled.Handled);
        Assert.Single(handler.Handled);
    }

    [Fact]
    public async Task CommandExceptionHandler_Defaults()
    {
        ICommandExceptionHandler handler = new PlainExceptionHandler();
        var context = new CommandExceptionHandlerContext(Context(new IOException()));

        await handler.HandleAsync(context, default);

        Assert.False(context.Handled);
        Assert.Null(context.Result);
        Assert.Throws<ArgumentNullException>(() => new PlainExceptionHandler().ShouldHandle(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => handler.HandleAsync(null!, default));
    }

    [Fact]
    public async Task CommandExceptionLogger_LogsEachExceptionOncePerInstance()
    {
        var first     = new CountingExceptionLogger();
        var second    = new CountingExceptionLogger();
        var exception = new InvalidDataException();
        var context   = new CommandExceptionLoggerContext(Context(exception));

        await ((ICommandExceptionLogger)first).LogAsync(context, default);
        await ((ICommandExceptionLogger)first).LogAsync(context, default);
        await ((ICommandExceptionLogger)second).LogAsync(context, default);
        await ((ICommandExceptionLogger)first).LogAsync(new CommandExceptionLoggerContext(Context(new InvalidDataException())), default);

        Assert.Equal(2, first.Count);
        Assert.Equal(1, second.Count);
        Assert.True(context.PropagateException);
    }

    [Fact]
    public async Task CommandExceptionLogger_ForeignMarker_LogsEveryTime()
    {
        var key = (string)typeof(CommandExceptionLogger)
            .GetField("LoggedByKey", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;

        var logger    = new CountingExceptionLogger();
        var exception = new InvalidDataException();
        exception.Data[key] = "foreign";

        var context = new CommandExceptionLoggerContext(Context(exception));
        await ((ICommandExceptionLogger)logger).LogAsync(context, default);
        await ((ICommandExceptionLogger)logger).LogAsync(context, default);

        Assert.Equal(2, logger.Count);
        Assert.IsType<string>(((IDictionary)exception.Data)[key]);
    }

    [Fact]
    public async Task CommandExceptionLogger_NullContext_Throws()
    {
        ICommandExceptionLogger logger = new CountingExceptionLogger();

        await Assert.ThrowsAsync<ArgumentNullException>(() => logger.LogAsync(null!, default));
        Assert.Throws<ArgumentNullException>(() => new CountingExceptionLogger().ShouldLog(null!));
    }

    [Fact]
    public void ExceptionContexts_ExposeFailure()
    {
        var session    = new TestSession();
        var request    = new PingRequest();
        var descriptor = new CommandHandlerDescriptor(Cmd.PingRequest);
        var exception  = new InvalidDataException();
        var context    = new CommandExceptionContext(exception, session, descriptor, request);

        var handler = new CommandExceptionHandlerContext(context);
        var logger  = new CommandExceptionLoggerContext(context);

        Assert.Same(exception, handler.Exception);
        Assert.Same(session, handler.Session);
        Assert.Same(descriptor, handler.Descriptor);
        Assert.Same(request, handler.Request);
        Assert.False(handler.Handled);
        Assert.Same(exception, logger.Exception);
        Assert.Same(session, logger.Session);
        Assert.Same(descriptor, logger.Descriptor);
        Assert.Same(request, logger.Request);
        Assert.Same(context, logger.ExceptionContext);
    }
}
