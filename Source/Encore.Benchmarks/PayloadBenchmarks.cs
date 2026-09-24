using BenchmarkDotNet.Attributes;
using Encore.Messaging;
using Encore.Server;
using Encore.Sessions;

namespace Encore.Benchmarks;

[MemoryDiagnoser]
public class PayloadBenchmarks
{
    private readonly BenchmarkSession _session = new();

    private byte[] _payload = [];
    private ICommandDispatcher _function   = null!;
    private ICommandDispatcher _controller = null!;

    [Params(16, 1024, 16384)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _payload = new DefaultMessageCodec().Encode(new EchoRequest { Text = new string('x', Size) });

        _function = new CommandDispatcher();
        _function.Map<ISession, EchoRequest, EchoResponse>((_, request) =>
            Task.FromResult(new EchoResponse { Text = request.Text }));

        _controller = new CommandDispatcher();
        _controller.Map<ISession, EchoController>(s => new EchoController(s));
    }

    [Benchmark(Baseline = true, Description = "Function handler echo")]
    public Task FunctionHandler() => _function.Dispatch(_session, _payload, default);

    [Benchmark(Description = "Controller echo")]
    public Task ControllerHandler() => _controller.Dispatch(_session, _payload, default);
}
