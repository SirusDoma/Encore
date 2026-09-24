using BenchmarkDotNet.Attributes;
using Encore.Messaging;

namespace Encore.Benchmarks;

[MemoryDiagnoser]
public class CodecBenchmarks
{
    private readonly DefaultMessageCodec _codec = new();

    private readonly PingRequest _ping           = new() { Value = 42 };
    private readonly EchoRequest _echo           = new() { Text = new string('x', 64) };
    private readonly InventoryRequest _inventory = InventoryRequest.Create(16);

    private byte[] _pingBytes      = [];
    private byte[] _echoBytes      = [];
    private byte[] _inventoryBytes = [];

    [GlobalSetup]
    public void Setup()
    {
        _codec.Register<PingRequest>();
        _codec.Register<EchoRequest>();
        _codec.Register<InventoryRequest>();

        _pingBytes      = _codec.Encode(_ping);
        _echoBytes      = _codec.Encode(_echo);
        _inventoryBytes = _codec.Encode(_inventory);
    }

    [Benchmark(Description = "Encode int message")]
    public byte[] EncodeSmall() => _codec.Encode(_ping);

    [Benchmark(Description = "Decode int message")]
    public IMessage? DecodeSmall() => _codec.Decode(_pingBytes);

    [Benchmark(Description = "Encode 64-char string message")]
    public byte[] EncodeString() => _codec.Encode(_echo);

    [Benchmark(Description = "Decode 64-char string message")]
    public IMessage? DecodeString() => _codec.Decode(_echoBytes);

    [Benchmark(Description = "Encode nested message (16 items)")]
    public byte[] EncodeComplex() => _codec.Encode(_inventory);

    [Benchmark(Description = "Decode nested message (16 items)")]
    public IMessage? DecodeComplex() => _codec.Decode(_inventoryBytes);
}
