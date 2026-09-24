using System.Net;
using System.Net.Sockets;
using Encore.Messaging;
using Encore.Sessions;
using Microsoft.Extensions.Options;

namespace Encore.Server;

public interface ITcpServer<TSession> : IDisposable
    where TSession : ITcpSession
{
    public Socket Socket { get; }

    public bool Active { get; }

    public TcpOptions Options { get; }

    void Start(int maxConnections = (int)SocketOptionName.MaxConnections);

    Task<TSession> AcceptSession(CancellationToken cancellationToken);
}

public interface ITcpServer : ITcpServer<TcpSession>;

public class TcpServer : TcpServer<TcpSession>, ITcpServer
{
    public TcpServer(
        IOptions<TcpOptions>   options,
        SessionFactory?        sessionFactory = null,
        IMessageFramerFactory? framerFactory  = null,
        ICommandDispatcher?    dispatcher     = null
    )
        : base(sessionFactory ?? new SessionFactory(options, framerFactory, dispatcher), options)
    {
    }
}

public class TcpServer<TSession> : ITcpServer<TSession>
    where TSession : ITcpSession
{
    private readonly TcpListener _listener;
    private readonly ISessionFactory<TSession> _factory;
    private CancellationTokenSource _cancellation = new();

    public TcpServer(ISessionFactory<TSession> factory, IOptions<TcpOptions> options)
    {
        _listener = new TcpListener(IPAddress.Parse(options.Value.Address), options.Value.Port);
        _factory  = factory;

        Options   = options.Value;
    }

    public Socket Socket => _listener.Server;

    public bool Active { get; private set; } = false;

    public TcpOptions Options { get; init; }

    public async Task<TSession> AcceptSession(CancellationToken cancellationToken)
    {
        if (!Active)
            throw new InvalidOperationException("Server is not started");

        var stopping = _cancellation;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping.Token);

        TcpClient client;
        TSession session;

        try
        {
            client = await _listener.AcceptTcpClientAsync(linked.Token);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or SocketException && stopping.IsCancellationRequested)
        {
            throw new OperationCanceledException("Server stopped", ex, stopping.Token);
        }

        try
        {
            session = _factory.CreateSession(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        if (!stopping.IsCancellationRequested)
            return session;

        session.Dispose();
        throw new OperationCanceledException(stopping.Token);
    }

    public void Start(int maxConnections = (int)SocketOptionName.MaxConnections)
    {
        if (Active)
            return;

        _cancellation = new CancellationTokenSource();
        _listener.Start(maxConnections);

        Active = true;
    }

    public Task Stop()
    {
        if (!Active)
            return Task.CompletedTask;

        Active = false;

        _cancellation.Cancel();
        _listener.Stop();

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        Stop();

        _cancellation.Dispose();
        _listener.Dispose();
    }
}
