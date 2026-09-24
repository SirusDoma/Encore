using System.Net.Sockets;
using Encore.Server;
using Encore.Sessions;

namespace Encore.Benchmarks;

public sealed class BenchmarkSession : ITcpSession
{
    public event EventHandler? Disconnected
    {
        add { }
        remove { }
    }

    public IDictionary<string, object> Properties { get; } = new Dictionary<string, object>();

    public Socket Socket => throw new NotSupportedException();

    public TcpOptions Options { get; } = new();

    public bool Connected => true;

    public bool Authorized => true;

    public void Authorize<T>(T token)
    {
    }

    public object GetAuthorizedToken() => "token";

    public T GetAuthorizedToken<T>() => (T)GetAuthorizedToken();

    public void Terminate()
    {
    }

    public Task Execute(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task WriteFrame(byte[] payload, CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask<byte[]> ReadFrame(CancellationToken cancellationToken) => throw new NotSupportedException();

    public void Dispose()
    {
    }
}
