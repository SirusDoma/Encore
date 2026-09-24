using BenchmarkDotNet.Attributes;
using Encore.Hosting.Extensions;
using Encore.Messaging;
using Encore.Server;
using Encore.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Encore.Benchmarks;

[MemoryDiagnoser]
public class DispatchBenchmarks
{
    private readonly BenchmarkSession _session = new();

    private byte[] _ping   = [];
    private byte[] _notify = [];
    private IHost? _host;

    private ICommandDispatcher _function          = null!;
    private ICommandDispatcher _functionNoResponse = null!;
    private ICommandDispatcher _command           = null!;
    private ICommandDispatcher _controller        = null!;
    private ICommandDispatcher _controllerAsync   = null!;
    private ICommandDispatcher _controllerHosted  = null!;
    private ICommandDispatcher _globalFilters     = null!;
    private ICommandDispatcher _attributeFilters  = null!;
    private ICommandDispatcher _authorize         = null!;
    private ICommandDispatcher _shortCircuit      = null!;
    private ICommandDispatcher _exception         = null!;
    private ICommandDispatcher _fanOut            = null!;

    private static Task<PingResponse> Pong(ISession session, PingRequest request) =>
        Task.FromResult(new PingResponse { Value = request.Value + 1 });

    private static ICommandDispatcher Create(Action<ICommandDispatcher> configure)
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();
        configure(dispatcher);

        return dispatcher;
    }

    [GlobalSetup]
    public void Setup()
    {
        _ping   = new DefaultMessageCodec().Encode(new PingRequest { Value = 41 });
        _notify = new DefaultMessageCodec().EncodeCommand(Cmd.NotifyRequest);

        _function           = Create(d => d.Map<ISession, PingRequest, PingResponse>(Pong));
        _functionNoResponse = Create(d => d.Map<ISession, PingRequest>((_, _) => Task.CompletedTask));
        _command            = Create(d => d.Map<ISession, Cmd>(Cmd.NotifyRequest, _ => Task.CompletedTask));
        _controller         = Create(d => d.Map<ISession, PingController>(s => new PingController(s)));
        _controllerAsync    = Create(d => d.Map<ISession, AsyncPingController>(s => new AsyncPingController(s)));
        _authorize          = Create(d => d.Map<ISession, SecurePingController>(s => new SecurePingController(s)));

        _globalFilters = Create(d => d
            .AddFilter<NoOpGlobalFilter>()
            .AddFilter<NoOpGlobalFilter>()
            .AddFilter<NoOpGlobalFilter>()
            .Map<ISession, PingRequest, PingResponse>(Pong));

        _attributeFilters = Create(d => d
            .AddFilter<NoOpGlobalFilter>()
            .Map<ISession, FilteredPingController>(s => new FilteredPingController(s)));

        _shortCircuit = Create(d => d
            .AddFilter<RejectFilter>()
            .Map<ISession, PingRequest, PingResponse>(Pong));

        _exception = Create(d => d
            .AddExceptionLogger<NoOpExceptionLogger>()
            .AddExceptionFilter<ErrorResponseHandler>()
            .Map<ISession, PingRequest, PingResponse>((_, _) => throw new InvalidOperationException("failed")));

        _fanOut = Create(d => d
            .Map<ISession, PingRequest, PingResponse>(Pong)
            .Map<ISession, PingRequest, PingResponse>(Pong)
            .Map<ISession, PingRequest, PingResponse>(Pong));

        _host = new HostBuilder()
            .ConfigureRoutes(routes => routes.Map<PingController>())
            .Build();
        _controllerHosted = _host.Services.GetRequiredService<ICommandDispatcher>();
    }

    [GlobalCleanup]
    public void Cleanup() => _host?.Dispose();

    [Benchmark(Baseline = true, Description = "Function handler (request → response)")]
    public Task FunctionHandler() => _function.Dispatch(_session, _ping, default);

    [Benchmark(Description = "Function handler (request only)")]
    public Task FunctionHandlerNoResponse() => _functionNoResponse.Dispatch(_session, _ping, default);

    [Benchmark(Description = "Command-only handler")]
    public Task CommandHandler() => _command.Dispatch(_session, _notify, default);

    [Benchmark(Description = "Controller (sync)")]
    public Task ControllerHandler() => _controller.Dispatch(_session, _ping, default);

    [Benchmark(Description = "Controller (async + cancellation)")]
    public Task ControllerHandlerAsync() => _controllerAsync.Dispatch(_session, _ping, default);

    [Benchmark(Description = "Controller via DI (scope per request)")]
    public Task ControllerHandlerHosted() => _controllerHosted.Dispatch(_session, _ping, default);

    [Benchmark(Description = "Function handler + 3 global filters")]
    public Task FunctionHandlerGlobalFilters() => _globalFilters.Dispatch(_session, _ping, default);

    [Benchmark(Description = "Controller + global/class/method filters")]
    public Task ControllerHandlerAttributeFilters() => _attributeFilters.Dispatch(_session, _ping, default);

    [Benchmark(Description = "Controller + [Authorize]")]
    public Task ControllerHandlerAuthorize() => _authorize.Dispatch(_session, _ping, default);

    [Benchmark(Description = "Filter short-circuit with result")]
    public Task FilterShortCircuit() => _shortCircuit.Dispatch(_session, _ping, default);

    [Benchmark(Description = "Handler exception → logger + handler result")]
    public Task HandledException() => _exception.Dispatch(_session, _ping, default);

    [Benchmark(Description = "Fan-out to 3 handlers")]
    public Task FanOut() => _fanOut.Dispatch(_session, _ping, default);
}
