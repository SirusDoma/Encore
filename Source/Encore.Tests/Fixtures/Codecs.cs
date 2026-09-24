using Encore.Messaging;

namespace Encore.Tests.Fixtures;

public sealed class TrackingCodec : IMessageCodec
{
    private readonly DefaultMessageCodec _inner = new();
    private int _decodeCount;

    public int DecodeCount => _decodeCount;

    public Func<byte[], Enum>? DecodeCommandOverride { get; set; }

    public Type? GetRegisteredType(Type? type) => _inner.GetRegisteredType(type);

    public void Register<T>() where T : class, IMessage => _inner.Register<T>();

    public void Register<T>(T command) where T : Enum => _inner.Register(command);

    public void Register<T>(T command, Type type) where T : Enum => _inner.Register(command, type);

    public void Register(Type type) => _inner.Register(type);

    public byte[] Encode<T>(T message) where T : class, IMessage => _inner.Encode(message);

    public byte[] Encode(IMessage message) => _inner.Encode(message);

    public byte[] EncodeCommand(Enum command) => _inner.EncodeCommand(command);

    public T Decode<T>(byte[] data) where T : class, IMessage, new() => _inner.Decode<T>(data);

    public IMessage? Decode(byte[] data)
    {
        Interlocked.Increment(ref _decodeCount);
        return _inner.Decode(data);
    }

    public Enum DecodeCommand(byte[] data) => DecodeCommandOverride?.Invoke(data) ?? _inner.DecodeCommand(data);
}
