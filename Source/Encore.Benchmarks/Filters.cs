using Encore.Server;

namespace Encore.Benchmarks;

public sealed class NoOpGlobalFilter : CommandFilter;

public sealed class NoOpFilterAttribute : CommandFilterAttribute;

public sealed class RejectFilter : CommandFilter
{
    private static readonly ErrorResponse Rejected = new() { Reason = "rejected" };

    public override void OnActionExecuting(CommandExecutingContext context)
    {
        context.Cancel = true;
        context.Result = Rejected;
    }
}

public sealed class ErrorResponseHandler : CommandExceptionHandler
{
    public override void Handle(CommandExceptionHandlerContext context) =>
        context.Result = new ErrorResponse { Reason = context.Exception.Message };
}

public sealed class NoOpExceptionLogger : CommandExceptionLogger;
