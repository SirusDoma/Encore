using Encore.Sessions;

namespace Encore.Tests.Sessions;

public class TcpSessionManagerTests
{
    private static TestTcpSession RunningSession(TaskCompletionSource? started = null) => new()
    {
        OnExecute = token =>
        {
            started?.TrySetResult();
            return TestTcpSession.UntilCancelled(token);
        },
    };

    private static TaskCompletionSource<T> Signal<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task StartSession_ExecutesAndTracksSession()
    {
        using var manager = new TcpSessionManager<TestTcpSession>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = RunningSession(started);
        TestTcpSession? raised = null;
        manager.Started += (_, e) => raised = e.Session;

        manager.StartSession(session);
        await started.Task.Within();

        Assert.Same(session, raised);
        Assert.True(manager.Validate(session));
        Assert.Equal([session], manager.GetSessions());
    }

    [Fact]
    public async Task StartSession_Twice_IsIgnored()
    {
        using var manager = new TcpSessionManager<TestTcpSession>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = RunningSession(started);
        var startedEvents = 0;
        manager.Started += (_, _) => startedEvents++;

        manager.StartSession(session);
        manager.StartSession(session);
        await started.Task.Within();

        Assert.Equal(1, startedEvents);
        Assert.Equal(1, session.ExecuteCount);
        Assert.Single(manager.GetSessions());
    }

    [Fact]
    public async Task StopSession_CancelsExecutionAndTerminates()
    {
        using var manager = new TcpSessionManager<TestTcpSession>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = RunningSession(started);
        var stoppedEvents = 0;
        manager.Stopped += (_, e) =>
        {
            Assert.Same(session, e.Session);
            Interlocked.Increment(ref stoppedEvents);
        };

        manager.StartSession(session);
        await started.Task.Within();
        await manager.StopSession(session).Within();

        Assert.False(manager.Validate(session));
        Assert.Empty(manager.GetSessions());
        Assert.Equal(1, stoppedEvents);
        Assert.Equal(1, session.TerminateCount);
    }

    [Fact]
    public async Task StopSession_Unknown_IsNoOp()
    {
        using var manager = new TcpSessionManager<TestTcpSession>();
        var stopped = false;
        manager.Stopped += (_, _) => stopped = true;

        await manager.StopSession(new TestTcpSession()).Within();

        Assert.False(stopped);
    }

    [Fact]
    public async Task SessionCompleting_IsRemovedAndTerminated()
    {
        using var manager = new TcpSessionManager<TestTcpSession>();
        var session = new TestTcpSession();
        var stopped = Signal<TestTcpSession>();
        manager.Stopped += (_, e) => stopped.TrySetResult(e.Session);

        manager.StartSession(session);

        Assert.Same(session, await stopped.Task.Within());
        await session.Terminated.Task.Within();
        Assert.False(manager.Validate(session));
    }

    [Fact]
    public async Task SessionFailure_RaisesErrorThenStops()
    {
        using var manager = new TcpSessionManager<TestTcpSession>();
        var failure = new InvalidDataException();
        var session = new TestTcpSession { OnExecute = _ => Task.FromException(failure) };
        var error   = Signal<Exception>();
        var stopped = Signal<bool>();
        manager.Error   += (_, e) =>
        {
            Assert.Same(session, e.Session);
            error.TrySetResult(e.Exception);
        };
        manager.Stopped += (_, _) => stopped.TrySetResult(true);

        manager.StartSession(session);

        Assert.Same(failure, await error.Task.Within());
        await stopped.Task.Within();
        await session.Terminated.Task.Within();
        Assert.Empty(manager.GetSessions());
    }

    [Fact]
    public async Task DisconnectedSession_IsStopped()
    {
        using var manager = new TcpSessionManager<TestTcpSession>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = RunningSession(started);
        var stopped = Signal<bool>();
        manager.Stopped += (_, _) => stopped.TrySetResult(true);

        manager.StartSession(session);
        await started.Task.Within();
        session.RaiseDisconnected();

        await stopped.Task.Within();
        await session.Terminated.Task.Within();
        Assert.False(manager.Validate(session));
    }

    [Fact]
    public async Task ClearSessions_StopsEverySession()
    {
        using var manager = new TcpSessionManager<TestTcpSession>();
        var sessions = Enumerable.Range(0, 3).Select(_ => RunningSession()).ToList();
        sessions.ForEach(manager.StartSession);

        await manager.ClearSessions().Within();

        Assert.Empty(manager.GetSessions());
        Assert.All(sessions, s => Assert.Equal(1, s.TerminateCount));
    }

    [Fact]
    public async Task Dispose_ClearsSessions()
    {
        var manager = new TcpSessionManager<TestTcpSession>();
        var session = RunningSession();
        manager.StartSession(session);

        manager.Dispose();

        await session.Terminated.Task.Within();
        Assert.Empty(manager.GetSessions());
    }
}
