using Encore.Server;
using Encore.Sessions;

namespace Encore.Benchmarks;

public sealed class PingController(ISession session) : CommandController(session)
{
    [CommandHandler]
    public PingResponse Ping(PingRequest request) => new() { Value = request.Value + 1 };
}

public sealed class AsyncPingController(ISession session) : CommandController(session)
{
    [CommandHandler]
    public Task<PingResponse> Ping(PingRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new PingResponse { Value = request.Value + 1 });
}

[NoOpFilter]
public sealed class FilteredPingController(ISession session) : CommandController(session)
{
    [NoOpFilter]
    [CommandHandler]
    public PingResponse Ping(PingRequest request) => new() { Value = request.Value + 1 };
}

[Authorize]
public sealed class SecurePingController(ISession session) : CommandController(session)
{
    [CommandHandler]
    public PingResponse Ping(PingRequest request) => new() { Value = request.Value + 1 };
}

public sealed class EchoController(ISession session) : CommandController(session)
{
    [CommandHandler]
    public EchoResponse Echo(EchoRequest request) => new() { Text = request.Text };
}
