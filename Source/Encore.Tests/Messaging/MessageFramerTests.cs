using System.Net.Sockets;
using Encore.Messaging;

namespace Encore.Tests.Messaging;

public class MessageFramerTests
{
    [Fact]
    public async Task WriteFrame_PrefixesLittleEndianSizeIncludingPrefix()
    {
        using var pair = await Loopback.CreateAsync();

        await new SizePrefixedMessageFramer<byte>(pair.Local.GetStream()).WriteFrame([1, 2, 3], default).Within();
        await new SizePrefixedMessageFramer<ushort>(pair.Local.GetStream()).WriteFrame([1, 2, 3], default).Within();
        await new SizePrefixedMessageFramer<uint>(pair.Local.GetStream()).WriteFrame([1, 2, 3], default).Within();

        Assert.Equal(Convert.FromHexString("04010203" + "0500010203" + "07000000010203"), await pair.Remote.ReadBytesAsync(16));
    }

    [Fact]
    public async Task WriteFrame_Memory_WritesSlice()
    {
        using var pair = await Loopback.CreateAsync();
        var framer = new SizePrefixedMessageFramer<ushort>(pair.Local.GetStream());

        await framer.WriteFrame(new byte[] { 9, 9, 1, 2 }.AsMemory(2), default).Within();

        Assert.Equal(Convert.FromHexString("04000102"), await pair.Remote.ReadBytesAsync(4));
    }

    [Fact]
    public async Task ReadFrame_ReadsPayload()
    {
        using var pair = await Loopback.CreateAsync();
        var framer = new SizePrefixedMessageFramer<ushort>(pair.Local.GetStream());

        await pair.Remote.WriteBytesAsync(Convert.FromHexString("0500010203"));

        Assert.Equal(new byte[] { 1, 2, 3 }, await framer.ReadFrame().Within());
    }

    [Fact]
    public async Task ReadFrame_InChunksSmallerThanPayload()
    {
        using var pair = await Loopback.CreateAsync();
        var framer = new SizePrefixedMessageFramer<ushort>(pair.Local.GetStream());

        await pair.Remote.WriteBytesAsync(Convert.FromHexString("07000102030405"));

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, await framer.ReadFrame(bufferSize: 2).Within());
    }

    [Fact]
    public async Task RoundTrip_BetweenPeers()
    {
        using var pair = await Loopback.CreateAsync();
        var local  = new SizePrefixedMessageFramer<int>(pair.Local.GetStream());
        var remote = new SizePrefixedMessageFramer<int>(pair.Remote.GetStream());
        byte[] payload = Enumerable.Range(0, 5000).Select(i => (byte)i).ToArray();

        await local.WriteFrame(payload, default).Within();
        await local.WriteFrame([42], default).Within();

        Assert.Equal(payload, await remote.ReadFrame().Within());
        Assert.Equal(new byte[] { 42 }, await remote.ReadFrame().Within());
    }

    [Theory]
    [InlineData("0000")]
    [InlineData("0200")]
    public async Task ReadFrame_EmptyFrame_ReturnsEmptyAndKeepsStreamAligned(string header)
    {
        using var pair = await Loopback.CreateAsync();
        var framer = new SizePrefixedMessageFramer<ushort>(pair.Local.GetStream());

        await pair.Remote.WriteBytesAsync(Convert.FromHexString(header + "030007"));

        Assert.Empty(await framer.ReadFrame().Within());
        Assert.Equal(new byte[] { 7 }, await framer.ReadFrame().Within());
    }

    [Fact]
    public async Task ReadFrame_SizeSmallerThanPrefix_Throws()
    {
        using var pair = await Loopback.CreateAsync();
        var framer = new SizePrefixedMessageFramer<ushort>(pair.Local.GetStream());

        await pair.Remote.WriteBytesAsync(Convert.FromHexString("0100"));

        await Assert.ThrowsAsync<FormatException>(() => framer.ReadFrame().Within());
    }

    [Fact]
    public async Task ReadFrame_LargerThanOneMegabyte_Throws()
    {
        using var pair = await Loopback.CreateAsync();
        var framer = new SizePrefixedMessageFramer<int>(pair.Local.GetStream());

        await pair.Remote.WriteBytesAsync(BitConverter.GetBytes(2 * 1024 * 1024 + 4));

        await Assert.ThrowsAsync<InvalidDataException>(() => framer.ReadFrame().Within());
    }

    [Fact]
    public async Task ReadFrame_NonPositiveBufferSize_Throws()
    {
        using var pair = await Loopback.CreateAsync();
        var framer = new SizePrefixedMessageFramer<ushort>(pair.Local.GetStream());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => framer.ReadFrame(bufferSize: 0));
    }

    [Fact]
    public async Task ReadFrame_PeerClosesMidFrame_Throws()
    {
        using var pair = await Loopback.CreateAsync();
        var framer = new SizePrefixedMessageFramer<ushort>(pair.Local.GetStream());

        await pair.Remote.WriteBytesAsync(Convert.FromHexString("050001"));
        pair.Remote.Close();

        await Assert.ThrowsAnyAsync<IOException>(() => framer.ReadFrame().Within());
    }

    [Fact]
    public async Task ReadFrame_Cancelled_Throws()
    {
        using var pair = await Loopback.CreateAsync();
        var framer = new SizePrefixedMessageFramer<ushort>(pair.Local.GetStream());
        using var cts = new CancellationTokenSource();

        var read = framer.ReadFrame(cancellationToken: cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.Within());
    }

    [Fact]
    public async Task WriteFrame_PayloadExceedingPrefixCapacity_Throws()
    {
        using var pair = await Loopback.CreateAsync();
        var framer = new SizePrefixedMessageFramer<byte>(pair.Local.GetStream());

        var ex = await Assert.ThrowsAsync<OverflowException>(() => framer.WriteFrame(new byte[300], default).AsTask());

        Assert.Equal("Failed to encode size prefix (300) to TSize", ex.Message);
        Assert.IsType<OverflowException>(ex.InnerException);
    }

    [Fact]
    public async Task UnreadableStream_ReadsNothing_UnwritableStream_WritesNothing()
    {
        using var pair = await Loopback.CreateAsync();
        using var writeOnly = new NetworkStream(pair.Local.Client, FileAccess.Write);
        using var readOnly  = new NetworkStream(pair.Local.Client, FileAccess.Read);

        Assert.Empty(await new SizePrefixedMessageFramer<ushort>(writeOnly).ReadFrame().Within());

        await new SizePrefixedMessageFramer<ushort>(readOnly).WriteFrame([1], default).Within();
        await new SizePrefixedMessageFramer<ushort>(pair.Local.GetStream()).WriteFrame([2], default).Within();

        Assert.Equal(Convert.FromHexString("030002"), await pair.Remote.ReadBytesAsync(3));
    }

    [Fact]
    public async Task Factory_CreatesSizePrefixedFramer()
    {
        using var pair = await Loopback.CreateAsync();

        var framer = new SizePrefixedMessageFramerFactory<ushort>().CreateFramer(pair.Local.GetStream());

        Assert.IsType<SizePrefixedMessageFramer<ushort>>(framer);
    }
}
