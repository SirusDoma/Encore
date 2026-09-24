using Encore.Messaging;

namespace Encore.Tests.Messaging;

public class MessageFieldAttributeTests
{
    [Fact]
    public void MessageField_Defaults()
    {
        var attribute = new MessageFieldAttribute(3);

        Assert.Equal(3, attribute.Order);
        Assert.Null(attribute.CodecType);
        Assert.Null(attribute.CodecArgs);
    }

    [Fact]
    public void MessageField_WithCodec_StoresCodecAndArgs()
    {
        var plain    = new MessageFieldAttribute(1, typeof(StringMessageFieldCodec));
        var withArgs = new MessageFieldAttribute(2, typeof(StringMessageFieldCodec), 5, "x");
        var generic  = new MessageFieldAttribute<MessageFieldCodec<short>>(4, 7);

        Assert.Equal(typeof(StringMessageFieldCodec), plain.CodecType);
        Assert.Null(plain.CodecArgs);
        Assert.Equal([5, "x"], withArgs.CodecArgs);
        Assert.Equal(4, generic.Order);
        Assert.Equal(typeof(MessageFieldCodec<short>), generic.CodecType);
        Assert.Equal([7], generic.CodecArgs);
    }

    [Fact]
    public void MessageField_NonCodecType_Throws()
    {
        Assert.Throws<ArgumentException>(() => new MessageFieldAttribute(0, typeof(string)));
        Assert.Throws<ArgumentException>(() => new MessageFieldAttribute(0, typeof(string), 1));
    }

    [Fact]
    public void StringMessageField_Defaults()
    {
        var attribute = new StringMessageFieldAttribute(2);

        Assert.Equal(2, attribute.Order);
        Assert.Equal(typeof(StringMessageFieldCodec), attribute.CodecType);
        Assert.Null(attribute.Encoding);
        Assert.Null(attribute.Format);
        Assert.Equal(ushort.MaxValue, attribute.MaxLength);
        Assert.True(attribute.NullTerminated);
        Assert.Equal(TypeCode.Empty, attribute.PrefixSizeType);
    }

    [Theory]
    [InlineData(TypeCode.SByte)]
    [InlineData(TypeCode.Byte)]
    [InlineData(TypeCode.Int16)]
    [InlineData(TypeCode.UInt16)]
    [InlineData(TypeCode.Int32)]
    [InlineData(TypeCode.UInt32)]
    [InlineData(TypeCode.Int64)]
    [InlineData(TypeCode.UInt64)]
    public void StringMessageField_AcceptsIntegerPrefix(TypeCode prefix)
    {
        Assert.Equal(prefix, new StringMessageFieldAttribute(prefixSizeType: prefix).PrefixSizeType);
    }

    [Theory]
    [InlineData(TypeCode.String)]
    [InlineData(TypeCode.Double)]
    [InlineData(TypeCode.Boolean)]
    public void StringMessageField_NonIntegerPrefix_Throws(TypeCode prefix)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StringMessageFieldAttribute(prefixSizeType: prefix));
    }

    [Fact]
    public void CollectionMessageField_Defaults()
    {
        var attribute = new CollectionMessageFieldAttribute(1);

        Assert.Equal(1, attribute.Order);
        Assert.Equal(typeof(CollectionMessageFieldCodec), attribute.CodecType);
        Assert.Equal(TypeCode.Empty, attribute.PrefixSizeType);
        Assert.Equal(0, attribute.MinCount);
        Assert.Equal(ushort.MaxValue, attribute.MaxCount);
        Assert.Null(attribute.ElementCodecType);
        Assert.Null(attribute.ElementCodecArgs);
    }

    [Fact]
    public void CollectionMessageField_Generic_StoresElementCodec()
    {
        var attribute = new CollectionMessageFieldAttribute<MessageFieldCodec<byte>>(0, TypeCode.Byte, 1, 4, "arg");

        Assert.Equal(typeof(CollectionMessageFieldCodec), attribute.CodecType);
        Assert.Equal(typeof(MessageFieldCodec<byte>), attribute.ElementCodecType);
        Assert.Equal(["arg"], attribute.ElementCodecArgs);
        Assert.Equal(1, attribute.MinCount);
        Assert.Equal(4, attribute.MaxCount);
    }

    [Fact]
    public void CollectionMessageField_InvalidBounds_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollectionMessageFieldAttribute(0, minCount: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollectionMessageFieldAttribute(0, maxCount: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollectionMessageFieldAttribute(0, minCount: 5, maxCount: 4));
    }
}
