using Encore.Messaging;

namespace Encore.Tests.Messaging;

public class StringMessageFieldCodecTests
{
    private sealed class ForeignAttribute : IMessageFieldAttribute
    {
        public int Order { get; set; }

        public Type? CodecType => null;

        public object?[]? CodecArgs => null;
    }

    private static StringMessageFieldCodec Codec(
        string? encoding = null, string? format = null, int maxLength = ushort.MaxValue,
        bool nullTerminated = true, TypeCode prefix = TypeCode.Empty) =>
        new(new StringMessageFieldAttribute(0, encoding, format, maxLength, nullTerminated, prefix));

    [Fact]
    public void Default_IsNullTerminatedUtf8()
    {
        var codec = Codec();

        byte[] bytes = Wire.Encode(codec, "abc");

        Assert.Equal(Convert.FromHexString("61626300"), bytes);
        Assert.Equal("abc", Wire.Decode(codec, bytes, typeof(string)));
    }

    [Fact]
    public void Decode_NullTerminated_StopsAtTerminator()
    {
        using var stream = new MemoryStream(Convert.FromHexString("6162007A"));
        var reader = new BinaryReader(stream);

        Assert.Equal("ab", Codec().Decode(reader, typeof(string)));
        Assert.Equal(3, stream.Position);
    }

    [Fact]
    public void PlainMessageFieldAttribute_IsUnterminatedAndReadsToEnd()
    {
        var codec = new StringMessageFieldCodec(new MessageFieldAttribute(0));

        byte[] bytes = Wire.Encode(codec, "abc");

        Assert.Equal(Convert.FromHexString("616263"), bytes);
        Assert.Equal("abc", Wire.Decode(codec, bytes, typeof(string)));
    }

    [Fact]
    public void Prefixed_Unterminated()
    {
        var codec = Codec(nullTerminated: false, prefix: TypeCode.UInt16);

        byte[] bytes = Wire.Encode(codec, "abc");

        Assert.Equal(Convert.FromHexString("0300616263"), bytes);
        Assert.Equal("abc", Wire.Decode(codec, bytes, typeof(string)));
    }

    [Fact]
    public void Prefixed_Terminated_CountsTerminator()
    {
        var codec = Codec(prefix: TypeCode.Byte);

        byte[] bytes = Wire.Encode(codec, "abc");

        Assert.Equal(Convert.FromHexString("0461626300"), bytes);
        Assert.Equal("abc", Wire.Decode(codec, bytes, typeof(string)));
    }

    [Theory]
    [InlineData("UTF-8",      "616200")]
    [InlineData("utf8",       "616200")]
    [InlineData("Unicode",    "610062000000")]
    [InlineData("UTF-16",     "610062000000")]
    [InlineData("UTF-16LE",   "610062000000")]
    [InlineData("UTF16",      "610062000000")]
    [InlineData("utf16le",    "610062000000")]
    [InlineData("UnicodeBE",  "006100620000")]
    [InlineData("UTF-16BE",   "006100620000")]
    [InlineData("UTF16BE",    "006100620000")]
    [InlineData("UTF-32",     "610000006200000000000000")]
    [InlineData("UTF32",      "610000006200000000000000")]
    [InlineData("ASCII",      "616200")]
    [InlineData("iso-8859-1", "616200")]
    public void Encoding_IsResolvedByName(string encoding, string expected)
    {
        var codec = Codec(encoding);

        byte[] bytes = Wire.Encode(codec, "ab");

        Assert.Equal(Convert.FromHexString(expected), bytes);
        Assert.Equal("ab", Wire.Decode(codec, bytes, typeof(string)));
    }

    [Fact]
    public void Encoding_FallsBackToSystemLookup()
    {
        Assert.Equal(Convert.FromHexString("E900"), Wire.Encode(Codec("iso-8859-1"), "é"));
    }

    [Fact]
    public void MaxLength_TruncatesIncludingTerminator()
    {
        Assert.Equal(Convert.FromHexString("61626300"), Wire.Encode(Codec(maxLength: 4), "abcdef"));
    }

    [Fact]
    public void Enum_IsWrittenByName()
    {
        var codec = Codec();

        byte[] bytes = Wire.Encode(codec, Cmd.PingRequest);

        Assert.Equal("PingRequest\0"u8.ToArray(), bytes);
        Assert.Equal(Cmd.PingRequest, Wire.Decode(codec, bytes, typeof(Cmd)));
    }

    [Fact]
    public void DateTime_UsesFormat()
    {
        var codec = Codec(format: "yyyyMMddHHmmss");
        var value = new DateTime(2024, 2, 29, 13, 37, 42);

        byte[] bytes = Wire.Encode(codec, value);

        Assert.Equal("20240229133742\0"u8.ToArray(), bytes);
        Assert.Equal(value, Wire.Decode(codec, bytes, typeof(DateTime)));
    }

    [Fact]
    public void DateTime_WithoutFormat_RoundTrips()
    {
        var codec = Codec();
        var value = new DateTime(2024, 2, 29, 13, 37, 42);

        Assert.Equal(value, Wire.Decode(codec, Wire.Encode(codec, value), typeof(DateTime)));
    }

    [Fact]
    public void TimeSpan_UsesFormat()
    {
        var codec = Codec(format: "c");
        var value = new TimeSpan(1, 2, 3);

        byte[] bytes = Wire.Encode(codec, value);

        Assert.Equal("01:02:03\0"u8.ToArray(), bytes);
        Assert.Equal(value, Wire.Decode(codec, bytes, typeof(TimeSpan)));
    }

    [Fact]
    public void TimeSpan_WithoutFormat_RoundTrips()
    {
        var codec = Codec();
        var value = new TimeSpan(4, 5, 6, 7);

        Assert.Equal(value, Wire.Decode(codec, Wire.Encode(codec, value), typeof(TimeSpan)));
    }

    [Fact]
    public void UnsupportedType_Throws()
    {
        Assert.Throws<NotSupportedException>(() => Wire.Encode(Codec(), 42));
        Assert.Throws<NotSupportedException>(() => Wire.Decode(Codec(), "42\0"u8.ToArray(), typeof(int)));
    }

    [Fact]
    public void Decode_MissingTerminator_Throws()
    {
        Assert.Throws<EndOfStreamException>(() => Wire.Decode(Codec(), Convert.FromHexString("6162"), typeof(string)));
    }

    [Fact]
    public void Decode_AtEndOfStream_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, Wire.Decode(Codec(), [], typeof(string)));
    }

    [Fact]
    public void Constructor_RejectsForeignAttribute()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StringMessageFieldCodec(new ForeignAttribute()));
    }
}
