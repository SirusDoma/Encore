using System.Collections;

namespace Encore.Server;

public interface ICommandExceptionLogger
{
    Task LogAsync(CommandExceptionLoggerContext context, CancellationToken cancellationToken);
}

public abstract class CommandExceptionLogger : ICommandExceptionLogger
{
    internal const string LoggedByKey = "__private__/ENCORE_EXCEPTION_LOGGED_BY";

    Task ICommandExceptionLogger.LogAsync(CommandExceptionLoggerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.ExceptionContext, nameof(context));

        if (!ShouldLog(context.ExceptionContext))
            return Task.CompletedTask;

        return LogAsync(context, cancellationToken);
    }

    public virtual Task LogAsync(CommandExceptionLoggerContext context, CancellationToken cancellationToken)
    {
        Log(context);
        return Task.CompletedTask;
    }

    public virtual void Log(CommandExceptionLoggerContext context)
    {
    }

    public virtual bool ShouldLog(CommandExceptionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var data = context.Exception.Data;
        if ((IDictionary?)data == null || data.IsReadOnly)
            return true;

        ICollection<object>? loggedBy;
        if (data.Contains(LoggedByKey))
        {
            object? untypedLoggedBy = data[LoggedByKey];
            loggedBy = untypedLoggedBy as ICollection<object>;

            if (loggedBy == null)
                return true;

            if (loggedBy.Contains(this))
                return false;
        }
        else
        {
            loggedBy = new List<object>();
            data.Add(LoggedByKey, loggedBy);
        }

        loggedBy.Add(this);
        return true;
    }
}
