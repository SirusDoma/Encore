using System.Net;
using System.Net.Sockets;
using BenchmarkDotNet.Attributes;
using Encore.Messaging;
using Encore.Server;
using Encore.Sessions;
using Microsoft.Extensions.Options;

namespace Encore.Benchmarks;

[MemoryDiagnoser]
public class TcpBenchmarks
{
    private const int Pipelined = 16;

    private sealed class Endpoint : IDisposable
    {
        private readonly TcpServer _server;
        private readonly TcpSessionManager _manager = new();
        private readonly TcpClient _client = new();

        public Endpoint(ICommandDispatcher dispatcher)
        {
            _server = new TcpServer(Options.Create(new TcpOptions { Port = 0 }), dispatcher: dispatcher);
            _server.Start();

            var accept = _server.AcceptSession(default);
            _client.NoDelay = true;
            _client.Connect(IPAddress.Loopback, ((IPEndPoint)_server.Socket.LocalEndPoint!).Port);

            var session = accept.GetAwaiter().GetResult();
            session.Socket.NoDelay = true;
            _manager.StartSession(session);

            Framer = new SizePrefixedMessageFramer<ushort>(_client.GetStream());
        }

        public SizePrefixedMessageFramer<ushort> Framer { get; }

        public void Dispose()
        {
            _client.Dispose();
            _manager.ClearSessions().Wait(TimeSpan.FromSeconds(5));
            _server.Dispose();
        }
    }

    private byte[] _ping = [];
    private Endpoint _function   = null!;
    private Endpoint _controller = null!;

    [GlobalSetup]
    public void Setup()
    {
        _ping = new DefaultMessageCodec().Encode(new PingRequest { Value = 41 });

        ICommandDispatcher function = new CommandDispatcher();
        function.Map<ISession, PingRequest, PingResponse>((_, request) =>
            Task.FromResult(new PingResponse { Value = request.Value + 1 }));

        ICommandDispatcher controller = new CommandDispatcher();
        controller.Map<ISession, PingController>(s => new PingController(s));

        _function   = new Endpoint(function);
        _controller = new Endpoint(controller);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _function.Dispose();
        _controller.Dispose();
    }

    [Benchmark(Baseline = true, Description = "Round-trip, function handler")]
    public async Task<byte[]> FunctionHandlerRoundTrip()
    {
        await _function.Framer.WriteFrame(_ping, default);
        return await _function.Framer.ReadFrame();
    }

    [Benchmark(Description = "Round-trip, controller")]
    public async Task<byte[]> ControllerHandlerRoundTrip()
    {
        await _controller.Framer.WriteFrame(_ping, default);
        return await _controller.Framer.ReadFrame();
    }

    [Benchmark(OperationsPerInvoke = Pipelined, Description = "Pipelined x16, function handler (per request)")]
    public async Task FunctionHandlerPipelined()
    {
        for (int i = 0; i < Pipelined; i++)
            await _function.Framer.WriteFrame(_ping, default);

        for (int i = 0; i < Pipelined; i++)
            await _function.Framer.ReadFrame();
    }
}
