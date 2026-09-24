using Encore.Messaging;

namespace Encore.Tests.Messaging;

public class MessageFieldCodecTests
{
    private enum Color : byte
    {
        Blue = 3,
    }

    private static readonly MessageFieldAttribute Attribute = new(0);

    [Fact]
    public void Encode_WritesNativeLittleEndianBytes()
    {
        Assert.Equal(Convert.FromHexString("04030201"), Wire.Encode(new MessageFieldCodec<int>(Attribute), 0x01020304));
        Assert.Equal(Convert.FromHexString("01"), Wire.Encode(new MessageFieldCodec<bool>(Attribute), true));
        Assert.Equal(Convert.FromHexString("4100"), Wire.Encode(new MessageFieldCodec<char>(Attribute), 'A'));
    }

    [Fact]
    public void Encode_ConvertsCompatibleValues()
    {
        Assert.Equal(Convert.FromHexString("0500"), Wire.Encode(new MessageFieldCodec<short>(Attribute), 5, typeof(int)));
        Assert.Equal(Convert.FromHexString("03"), Wire.Encode(new MessageFieldCodec<byte>(Attribute), Color.Blue));
    }

    [Fact]
    public void Encode_OutOfRangeValue_Throws()
    {
        Assert.Throws<OverflowException>(() => Wire.Encode(new MessageFieldCodec<byte>(Attribute), 300));
    }

    [Fact]
    public void Decode_ReadsTypedValue()
    {
        object value = Wire.Decode(new MessageFieldCodec<ushort>(Attribute), Convert.FromHexString("3412"), typeof(ushort));

        Assert.Equal((ushort)0x1234, Assert.IsType<ushort>(value));
    }

    [Fact]
    public void Decode_InsufficientBytes_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Wire.Decode(new MessageFieldCodec<int>(Attribute), Convert.FromHexString("0102"), typeof(int)));
    }

    [Fact]
    public void EnumCodec_RoundTrips()
    {
        var codec = new MessageFieldCodec<Color>(Attribute);

        byte[] bytes = Wire.Encode(codec, Color.Blue);

        Assert.Equal(Convert.FromHexString("03"), bytes);
        Assert.Equal(Color.Blue, Wire.Decode(codec, bytes, typeof(Color)));
    }

    [Fact]
    public void Attribute_IsExposed()
    {
        Assert.Same(Attribute, new MessageFieldCodec<int>(Attribute).Attribute);
    }
}
