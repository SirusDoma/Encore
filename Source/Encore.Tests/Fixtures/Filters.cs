using Encore.Server;

namespace Encore.Tests.Fixtures;

public sealed class LogFilterAttribute(string name) : CommandFilterAttribute
{
    public override void OnActionExecuting(CommandExecutingContext context) =>
        ((TestSession)context.Session).Record($"{name}:executing");

    public override void OnActionExecuted(CommandExecutedContext context) =>
        ((TestSession)context.Session).Record($"{name}:executed");
}

public sealed class DelegateFilter : ICommandFilter
{
    public Action<CommandExecutingContext>? Executing { get; init; }

    public Action<CommandExecutedContext>? Executed { get; init; }

    public Task OnActionExecutingAsync(CommandExecutingContext context, CancellationToken cancellationToken = default)
    {
        Executing?.Invoke(context);
        return Task.CompletedTask;
    }

    public Task OnActionExecutedAsync(CommandExecutedContext context, CancellationToken cancellationToken = default)
    {
        Executed?.Invoke(context);
        return Task.CompletedTask;
    }
}

public sealed class DelegateExceptionHandler(Action<CommandExceptionHandlerContext> handle) : ICommandExceptionHandler
{
    public Task HandleAsync(CommandExceptionHandlerContext context, CancellationToken cancellationToken)
    {
        handle(context);
        return Task.CompletedTask;
    }
}

public sealed class DelegateExceptionLogger(Action<CommandExceptionLoggerContext> log) : ICommandExceptionLogger
{
    public Task LogAsync(CommandExceptionLoggerContext context, CancellationToken cancellationToken)
    {
        log(context);
        return Task.CompletedTask;
    }
}
