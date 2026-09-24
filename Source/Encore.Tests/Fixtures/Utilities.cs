using System.Net;
using System.Net.Sockets;
using Encore.Messaging;

namespace Encore.Tests.Fixtures;

public static class Wire
{
    public static byte[] Encode<T>(T message) where T : class, IMessage => new DefaultMessageCodec().Encode(message);

    public static T Decode<T>(byte[] data) where T : class, IMessage, new() => new DefaultMessageCodec().Decode<T>(data);

    public static byte[] Encode(MessageFieldCodec codec, object value, Type? type = null)
    {
        using var stream = new MemoryStream();
        codec.Encode(new BinaryWriter(stream), value, type ?? value.GetType());

        return stream.ToArray();
    }

    public static object Decode(MessageFieldCodec codec, byte[] data, Type type)
    {
        using var stream = new MemoryStream(data);
        return codec.Decode(new BinaryReader(stream), type);
    }
}

public sealed class Loopback : IDisposable
{
    private Loopback(TcpClient local, TcpClient remote)
    {
        Local  = local;
        Remote = remote;
    }

    public TcpClient Local { get; }

    public TcpClient Remote { get; }

    public static async Task<Loopback> CreateAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            var remote = new TcpClient();
            var accept = listener.AcceptTcpClientAsync();
            await remote.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port).Within();

            return new Loopback(await accept.Within(), remote);
        }
        finally
        {
            listener.Stop();
        }
    }

    public void Dispose()
    {
        Local.Dispose();
        Remote.Dispose();
    }
}

public static class TestExtensions
{
    public static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    public static Task Within(this Task task) => task.WaitAsync(TestTimeout);

    public static Task<T> Within<T>(this Task<T> task) => task.WaitAsync(TestTimeout);

    public static Task<T> Within<T>(this ValueTask<T> task) => task.AsTask().WaitAsync(TestTimeout);

    public static Task Within(this ValueTask task) => task.AsTask().WaitAsync(TestTimeout);

    public static async Task<byte[]> ReadBytesAsync(this TcpClient client, int count)
    {
        byte[] buffer = new byte[count];
        await client.GetStream().ReadExactlyAsync(buffer).AsTask().Within();

        return buffer;
    }

    public static async Task WriteBytesAsync(this TcpClient client, byte[] data)
    {
        await client.GetStream().WriteAsync(data).AsTask().Within();
    }

    public static Task<int> ReadAnyAsync(this TcpClient client) =>
        client.GetStream().ReadAsync(new byte[1]).AsTask().Within();
}
