using Encore.Messaging;
using Encore.Server;
using Encore.Sessions;

namespace Encore.Tests.Server;

public class CommandFilterTests
{
    private sealed class RecordingFilter(string name) : CommandFilter
    {
        public override void OnActionExecuting(CommandExecutingContext context) =>
            ((TestSession)context.Session).Record($"{name}:executing");

        public override void OnActionExecuted(CommandExecutedContext context) =>
            ((TestSession)context.Session).Record($"{name}:executed");
    }

    private sealed class DefaultFilter : CommandFilter
    {
        public override void OnActionExecuting(CommandExecutingContext context) =>
            ((TestSession)context.Session).Record("default:executing");
    }

    private sealed class PlainFilter : CommandFilter;

    private sealed class PlainFilterAttribute : CommandFilterAttribute;

    private static ICommandDispatcher CreateDispatcher(Func<TestSession, PingRequest, PingResponse>? handler = null)
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();
        dispatcher.Map<TestSession, PingRequest, PingResponse>((session, request) =>
        {
            session.Record("handler");
            return Task.FromResult(handler?.Invoke(session, request) ?? new PingResponse { Value = request.Value });
        });

        return dispatcher;
    }

    [Fact]
    public async Task GlobalFilters_RunAroundHandlerInRegistrationOrder()
    {
        var dispatcher = CreateDispatcher();
        var session    = new TestSession();

        dispatcher.AddFilter(new RecordingFilter("a"));
        dispatcher.AddFilter(new RecordingFilter("b"));
        dispatcher.AddFilter<DefaultFilter>();

        await dispatcher.Dispatch(session, new PingRequest(), default);

        Assert.Equal(
            ["a:executing", "b:executing", "default:executing", "handler", "a:executed", "b:executed"],
            session.Log);
    }

    [Fact]
    public async Task Filter_ReceivesExecutionContext()
    {
        var dispatcher = CreateDispatcher();
        var session    = new TestSession();
        var request    = new PingRequest { Value = 5 };
        CommandExecutingContext? executing = null;
        CommandExecutedContext? executed   = null;

        dispatcher.AddFilter(new DelegateFilter { Executing = c => executing = c, Executed = c => executed = c });

        await dispatcher.Dispatch(session, request, default);

        Assert.Same(session, executing!.Session);
        Assert.Equal<Enum>(Cmd.PingRequest, executing.Command);
        Assert.Same(request, executing.Request);
        Assert.Equal(typeof(PingRequest), executing.Descriptor.RequestType);
        Assert.Equal(typeof(PingResponse), executing.Descriptor.ResponseType);
        Assert.False(executing.Cancel);
        Assert.Null(executed!.Exception);
        Assert.False(executed.ExceptionHandled);
    }

    [Fact]
    public async Task CancelWithoutResult_SkipsHandlerRemainingFiltersAndResponse()
    {
        var dispatcher = CreateDispatcher();
        var session    = new TestSession();

        dispatcher.AddFilter(new DelegateFilter { Executing = c => c.Cancel = true });
        dispatcher.AddFilter(new RecordingFilter("after"));

        await dispatcher.Dispatch(session, new PingRequest(), default);

        Assert.Empty(session.Log);
        Assert.Empty(session.Frames);
    }

    [Fact]
    public async Task CancelWithResult_WritesResultInsteadOfHandlerResponse()
    {
        var dispatcher = CreateDispatcher();
        var session    = new TestSession();

        dispatcher.AddFilter(new DelegateFilter
        {
            Executing = c =>
            {
                c.Cancel = true;
                c.Result = new FailureResponse { Reason = "denied" };
            },
        });

        await dispatcher.Dispatch(session, new PingRequest(), default);

        Assert.Empty(session.Log);
        Assert.Equal("denied", Wire.Decode<FailureResponse>(Assert.Single(session.Frames)).Reason);
    }

    [Fact]
    public async Task ExecutedResult_ReplacesHandlerResponse()
    {
        var dispatcher = CreateDispatcher();
        var session    = new TestSession();

        dispatcher.AddFilter(new DelegateFilter { Executed = c => c.Result = new FailureResponse { Reason = "override" } });

        await dispatcher.Dispatch(session, new PingRequest(), default);

        Assert.Equal("override", Wire.Decode<FailureResponse>(Assert.Single(session.Frames)).Reason);
    }

    [Fact]
    public async Task Executed_SeesHandlerExceptionAndCanHandleIt()
    {
        var dispatcher = CreateDispatcher((_, _) => throw new InvalidDataException());
        var session    = new TestSession();
        Exception? seen = null;

        dispatcher.AddFilter(new DelegateFilter
        {
            Executed = c =>
            {
                seen = c.Exception;
                c.ExceptionHandled = true;
            },
        });

        await dispatcher.Dispatch(session, new PingRequest(), default);

        Assert.IsType<InvalidDataException>(seen);
        Assert.Empty(session.Frames);
    }

    [Fact]
    public async Task Executed_CanHandleExceptionWithResult()
    {
        var dispatcher = CreateDispatcher((_, _) => throw new InvalidDataException());
        var session    = new TestSession();

        dispatcher.AddFilter(new DelegateFilter
        {
            Executed = c =>
            {
                c.ExceptionHandled = true;
                c.Result           = new FailureResponse { Reason = c.Exception!.GetType().Name };
            },
        });

        await dispatcher.Dispatch(session, new PingRequest(), default);

        Assert.Equal(nameof(InvalidDataException), Wire.Decode<FailureResponse>(Assert.Single(session.Frames)).Reason);
    }

    [Fact]
    public async Task Executed_UnhandledException_Propagates()
    {
        var dispatcher = CreateDispatcher((_, _) => throw new InvalidDataException());
        var executedCount = 0;

        dispatcher.AddFilter(new DelegateFilter { Executed = _ => executedCount++ });

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            dispatcher.Dispatch(new TestSession(), new PingRequest(), default));
        Assert.Equal(1, executedCount);
    }

    [Fact]
    public async Task ExecutedFilterThrowing_AfterHandlerFailure_AggregatesBoth()
    {
        var dispatcher = CreateDispatcher((_, _) => throw new InvalidDataException());

        dispatcher.AddFilter(new DelegateFilter { Executed = _ => throw new TimeoutException() });

        var ex = await Assert.ThrowsAsync<AggregateException>(() =>
            dispatcher.Dispatch(new TestSession(), new PingRequest(), default));

        Assert.Collection(ex.InnerExceptions,
            e => Assert.IsType<TimeoutException>(e),
            e => Assert.IsType<InvalidDataException>(e));
    }

    [Fact]
    public async Task ExecutedFilterThrowing_AfterSuccess_Propagates()
    {
        var dispatcher = CreateDispatcher();

        dispatcher.AddFilter(new DelegateFilter { Executed = _ => throw new TimeoutException() });

        await Assert.ThrowsAsync<TimeoutException>(() =>
            dispatcher.Dispatch(new TestSession(), new PingRequest(), default));
    }

    [Fact]
    public async Task ExecutingFilterThrowing_ReachesExceptionFiltersWithDescriptor()
    {
        var dispatcher = CreateDispatcher();
        var session    = new TestSession();
        CommandExceptionLoggerContext? logged = null;

        dispatcher.AddFilter(new DelegateFilter { Executing = _ => throw new TimeoutException() });
        dispatcher.AddExceptionLogger(new DelegateExceptionLogger(c => logged = c));

        await Assert.ThrowsAsync<TimeoutException>(() => dispatcher.Dispatch(session, new PingRequest(), default));

        Assert.Same(session, logged!.Session);
        Assert.Equal(typeof(PingRequest), logged.Descriptor?.RequestType);
        Assert.IsType<PingRequest>(logged.Request);
        Assert.Empty(session.Log);
    }

    [Fact]
    public async Task BaseFilters_DefaultToNoOp()
    {
        var context   = new CommandExecutionContext(new TestSession(), Cmd.PingRequest, null, new CommandHandlerDescriptor(Cmd.PingRequest));
        var executing = new CommandExecutingContext(context);
        var executed  = new CommandExecutedContext(context, null);

        ICommandFilter filter = new PlainFilter();
        await filter.OnActionExecutingAsync(executing);
        await filter.OnActionExecutedAsync(executed);

        ICommandFilter attribute = new PlainFilterAttribute();
        await attribute.OnActionExecutingAsync(executing);
        await attribute.OnActionExecutedAsync(executed);

        Assert.False(executing.Cancel);
        Assert.Null(executing.Result);
        Assert.Null(executed.Result);
        Assert.Equal(0, new PlainFilter().Order);
        Assert.Equal(0, new PlainFilterAttribute().Order);
    }

    [Fact]
    public void Contexts_CopyExecutionState()
    {
        var session    = new TestSession();
        var request    = new PingRequest();
        var descriptor = new CommandHandlerDescriptor(Cmd.PingRequest, typeof(PingRequest));
        var context    = new CommandExecutionContext(session, Cmd.PingRequest, request, descriptor)
        {
            Result = new PingResponse(),
        };
        var exception  = new InvalidDataException();

        var executing = new CommandExecutingContext(context);
        var executed  = new CommandExecutedContext(context, exception);

        foreach (var copy in new CommandExecutionContext[] { executing, executed })
        {
            Assert.Same(session, copy.Session);
            Assert.Equal<Enum>(Cmd.PingRequest, copy.Command);
            Assert.Same(request, copy.Request);
            Assert.Same(descriptor, copy.Descriptor);
            Assert.Null(copy.Result);
        }

        Assert.Same(exception, executed.Exception);
        Assert.Throws<ArgumentNullException>(() => new CommandExecutionContext(null!, Cmd.PingRequest, null, descriptor));
    }
}
