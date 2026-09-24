using System.Net.Sockets;
using Encore.Server;
using Encore.Sessions;

namespace Encore.Tests.Fixtures;

public class TestSession : ISession
{
    private readonly List<byte[]> _frames = [];
    private readonly List<string> _log = [];

    public IDictionary<string, object> Properties { get; } = new Dictionary<string, object>();

    public IReadOnlyList<byte[]> Frames
    {
        get { lock (_frames) return _frames.ToList(); }
    }

    public IReadOnlyList<string> Log
    {
        get { lock (_log) return _log.ToList(); }
    }

    public void Record(string entry)
    {
        lock (_log)
            _log.Add(entry);
    }

    public virtual Task Execute(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task WriteFrame(byte[] payload, CancellationToken cancellationToken)
    {
        lock (_frames)
            _frames.Add(payload);

        return Task.CompletedTask;
    }
}

public sealed class TestTcpSession : TestSession, ITcpSession
{
    private object? _token;
    private int _executeCount;
    private int _terminateCount;

    public event EventHandler? Disconnected;

    public Func<CancellationToken, Task> OnExecute { get; set; } = _ => Task.CompletedTask;

    public TaskCompletionSource Terminated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int ExecuteCount => _executeCount;

    public int TerminateCount => _terminateCount;

    public Socket Socket => throw new NotSupportedException();

    public TcpOptions Options { get; } = new();

    public bool Connected { get; set; }

    public bool Authorized => _token != null;

    public void Authorize<T>(T token) => _token = token;

    public object GetAuthorizedToken() => _token ?? throw new InvalidOperationException("Unauthorized");

    public T GetAuthorizedToken<T>() => (T)GetAuthorizedToken();

    public override Task Execute(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _executeCount);
        return OnExecute(cancellationToken);
    }

    public void Terminate()
    {
        Interlocked.Increment(ref _terminateCount);
        Terminated.TrySetResult();
    }

    public ValueTask<byte[]> ReadFrame(CancellationToken cancellationToken) => throw new NotSupportedException();

    public void RaiseDisconnected() => Disconnected?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
    }

    public static async Task UntilCancelled(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
