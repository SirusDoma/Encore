using System.Text;

namespace Encore.Tests.Utilities;

public class StreamExtensionsTests
{
    private static byte[] Write(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        write(new BinaryWriter(stream));

        return stream.ToArray();
    }

    private static BinaryReader Reader(string hex) => new(new MemoryStream(Convert.FromHexString(hex)));

    [Theory]
    [InlineData(TypeCode.SByte, 1)]
    [InlineData(TypeCode.Byte, 1)]
    [InlineData(TypeCode.Int16, 2)]
    [InlineData(TypeCode.UInt16, 2)]
    [InlineData(TypeCode.Int32, 4)]
    [InlineData(TypeCode.UInt32, 4)]
    [InlineData(TypeCode.Int64, 8)]
    [InlineData(TypeCode.UInt64, 8)]
    public void Integer_RoundTripsWithRequestedWidth(TypeCode code, int size)
    {
        byte[] bytes = Write(writer => writer.WriteInteger(42, code));

        Assert.Equal(size, bytes.Length);

        object value = new BinaryReader(new MemoryStream(bytes)).ReadInteger(code);
        Assert.Equal(code, Type.GetTypeCode(value.GetType()));
        Assert.Equal(42, Convert.ToInt32(value));
    }

    [Theory]
    [InlineData(TypeCode.Double)]
    [InlineData(TypeCode.String)]
    [InlineData(TypeCode.Boolean)]
    public void Integer_UnsupportedTypeCode_Throws(TypeCode code)
    {
        Assert.Throws<NotSupportedException>(() => Write(writer => writer.WriteInteger(1, code)));
        Assert.Throws<NotSupportedException>(() => Reader("0000000000000000").ReadInteger(code));
    }

    [Fact]
    public void WriteInteger_OutOfRange_Throws()
    {
        Assert.Throws<OverflowException>(() => Write(writer => writer.WriteInteger(300, TypeCode.Byte)));
    }

    [Fact]
    public void WriteString_DefaultsToNullTerminated()
    {
        int count = 0;
        byte[] bytes = Write(writer => count = writer.Write("abc", Encoding.UTF8));

        Assert.Equal(Convert.FromHexString("61626300"), bytes);
        Assert.Equal(4, count);
    }

    [Fact]
    public void WriteString_Unterminated()
    {
        Assert.Equal(Convert.FromHexString("616263"),
            Write(writer => writer.Write("abc", Encoding.UTF8, terminateWithNull: false)));
    }

    [Fact]
    public void WriteString_PrefixCountsTerminator()
    {
        Assert.Equal(Convert.FromHexString("0400000061626300"),
            Write(writer => writer.Write("abc", Encoding.UTF8, TypeCode.Int32)));
    }

    [Fact]
    public void WriteString_DoesNotDuplicateTrailingTerminator()
    {
        Assert.Equal(Convert.FromHexString("61626300"), Write(writer => writer.Write("abc\0", Encoding.UTF8)));
    }

    [Fact]
    public void WriteString_TrailingTerminatorIsCutByTruncation()
    {
        int count = 0;
        byte[] bytes = Write(writer => count = writer.Write("abcdef\0", Encoding.UTF8, maxCount: 4));

        Assert.Equal(Convert.FromHexString("61626364"), bytes);
        Assert.Equal(4, count);
    }

    [Fact]
    public void WriteString_TruncatesToMaxCount()
    {
        Assert.Equal(Convert.FromHexString("616200"), Write(writer => writer.Write("abcdef", Encoding.UTF8, maxCount: 3)));
        Assert.Equal(Convert.FromHexString("616263"),
            Write(writer => writer.Write("abcdef", Encoding.UTF8, terminateWithNull: false, maxCount: 3)));
    }

    [Fact]
    public void WriteString_ZeroMaxCount_WritesNothing()
    {
        int count = -1;
        byte[] bytes = Write(writer => count = writer.Write("abc", Encoding.UTF8, maxCount: 0));

        Assert.Empty(bytes);
        Assert.Equal(0, count);
    }

    [Fact]
    public void WriteString_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Write(writer => writer.Write("a", Encoding.Unicode, maxCount: 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Write(writer => writer.Write("a", Encoding.UTF8, maxCount: -1)));
        Assert.Throws<ArgumentNullException>(() => Write(writer => writer.Write(null!, Encoding.UTF8)));
        Assert.Throws<ArgumentNullException>(() => Write(writer => writer.Write("a", null!)));
        Assert.Throws<ArgumentNullException>(() => ((BinaryWriter)null!).Write("a", Encoding.UTF8));
    }

    [Fact]
    public void ReadString_NullTerminated_StopsAfterTerminator()
    {
        var reader = Reader("6162007A");

        Assert.Equal("ab", reader.ReadString(Encoding.UTF8));
        Assert.Equal(3, reader.BaseStream.Position);
    }

    [Fact]
    public void ReadString_NullTerminatedWideEncoding_UsesWideTerminator()
    {
        Assert.Equal("ab", Reader("610062000000").ReadString(Encoding.Unicode));
    }

    [Fact]
    public void ReadString_Prefixed_ReadsDeclaredLength()
    {
        var reader = Reader("0261626300");

        Assert.Equal("ab", reader.ReadString(Encoding.UTF8, TypeCode.Byte));
        Assert.Equal(3, reader.BaseStream.Position);
    }

    [Fact]
    public void ReadString_Unterminated_ReadsRemainingUpToMaxCount()
    {
        Assert.Equal("abc", Reader("616263").ReadString(Encoding.UTF8, nullTerminated: false));
        Assert.Equal("ab", Reader("616263").ReadString(Encoding.UTF8, nullTerminated: false, maxCount: 2));
    }

    [Fact]
    public void ReadString_AtEndOfStream_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, Reader("").ReadString(Encoding.UTF8));
    }

    [Fact]
    public void ReadString_InvalidInput_Throws()
    {
        Assert.Throws<FormatException>(() => Reader("FF").ReadString(Encoding.UTF8, TypeCode.SByte));
        Assert.Throws<FormatException>(() => Reader("0561").ReadString(Encoding.UTF8, TypeCode.Byte));
        Assert.Throws<EndOfStreamException>(() => Reader("6162").ReadString(Encoding.UTF8));
        Assert.Throws<ArgumentOutOfRangeException>(() => Reader("00").ReadString(Encoding.UTF8, maxCount: -1));
        Assert.Throws<ArgumentNullException>(() => Reader("00").ReadString(null!));
    }
}
