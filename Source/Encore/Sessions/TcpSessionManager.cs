namespace Encore.Sessions;

public interface ITcpSessionManager : ITcpSessionManager<TcpSession>;

public interface ITcpSessionManager<TSession> : ISessionManager<TSession>
    where TSession : ITcpSession
{

    Task StopSession(TSession session);

    bool Validate(TSession session);

    IReadOnlyList<TSession> GetSessions();

    Task ClearSessions();
}

public class TcpSessionManager<TSession> : ITcpSessionManager<TSession>
    where TSession : ITcpSession
{
    private class ManagedSession
    {
        public required TSession Session { get; init; }

        public required Task Execution { get; init; }

        public required CancellationTokenSource CancellationTokenSource { get; init; }

    }
    private readonly List<ManagedSession> _sessions = [];
    private readonly Lock _lock = new();

    public event EventHandler<SessionEventArgs<TSession>>? Started;

    public event EventHandler<SessionEventArgs<TSession>>? Stopped;

    public event EventHandler<SessionErrorEventArgs<TSession>>? Error;

    public TcpSessionManager()
    {
    }

    public virtual void StartSession(TSession session)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            if (_sessions.Any(m => ReferenceEquals(m.Session, session)))
                return;

            var cancellationTokenSource = new CancellationTokenSource();
            session.Disconnected += OnSessionDisconnected;

            var execution = Task.Run(async () =>
            {
                await completion.Task.ConfigureAwait(false);

                try
                {
                    await session.Execute(cancellationTokenSource.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Error?.Invoke(this, new SessionErrorEventArgs<TSession>()
                    {
                        Session   = session,
                        Exception = ex
                    });
                }
                finally
                {
                    try
                    {
                        _ = StopSession(session);
                    }
                    finally
                    {
                        session.Terminate();
                    }
                }
            });

            _sessions.Add(new ManagedSession
            {
                Session = session,
                Execution = execution,
                CancellationTokenSource = cancellationTokenSource
            });
        }

        completion.SetResult();
        Started?.Invoke(this, new SessionEventArgs<TSession> { Session = session });
    }

    public virtual Task StopSession(TSession session)
    {
        ManagedSession? managed;
        lock (_lock)
        {
            managed = _sessions.FirstOrDefault(m => ReferenceEquals(m.Session, session));
            if (managed == null)
                return Task.CompletedTask;

            managed.Session.Disconnected -= OnSessionDisconnected;
            _sessions.Remove(managed);
        }

        managed.CancellationTokenSource.Cancel();

        Stopped?.Invoke(this, new SessionEventArgs<TSession> { Session = session });

        return managed.Execution;
    }

    public virtual bool Validate(TSession session)
    {
        lock (_lock)
            return _sessions.Any(m => ReferenceEquals(m.Session, session));
    }

    public virtual IReadOnlyList<TSession> GetSessions()
    {
        lock (_lock)
            return _sessions.Select(m => m.Session).ToList();
    }

    public Task ClearSessions()
    {
        ManagedSession[] sessions;
        lock (_lock)
            sessions = _sessions.ToArray();

        foreach (var managed in sessions)
            StopSession(managed.Session);

        return Task.WhenAll(sessions.Select(m => m.Execution));
    }

    private void OnSessionDisconnected(object? sender, EventArgs e)
    {
        StopSession((TSession)sender!);
    }

    public virtual void Dispose()
        => ClearSessions();
}

public sealed class TcpSessionManager : TcpSessionManager<TcpSession>;
