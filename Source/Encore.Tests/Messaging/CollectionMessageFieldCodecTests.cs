using Encore.Messaging;

namespace Encore.Tests.Messaging;

public class CollectionMessageFieldCodecTests
{
    public enum CollectionCmd : ushort
    {
        ListsRequest        = 0x0201,
        FixedRequest        = 0x0202,
        BoundedRequest      = 0x0203,
        SignedRequest       = 0x0204,
        EquipmentRequest    = 0x0205,
        IndexedRequest      = 0x0206,
        ElementCodecRequest = 0x0207,
        BadElementRequest   = 0x0208,
        UnknownRequest      = 0x0209,
        BlobRequest         = 0x020A,
        CharsRequest        = 0x020B,
        BoolsRequest        = 0x020C,
    }

    public enum Color : byte
    {
        Red   = 1,
        Green = 2,
        Blue  = 3,
    }

    public enum Slot
    {
        Head,
        Body,
        Tail,
    }

    public sealed class Tag : SubMessage
    {
        [StringMessageField(0)]
        public string Name { get; set; } = string.Empty;
    }

    public sealed record Point(short X, short Y);

    public sealed class PointCodec(IMessageFieldAttribute attribute) : MessageFieldCodec(attribute)
    {
        public override void Encode(BinaryWriter writer, object value, Type sourceType)
        {
            var point = (Point)value;
            writer.Write(point.X);
            writer.Write(point.Y);
        }

        public override object Decode(BinaryReader reader, Type targetType) =>
            new Point(reader.ReadInt16(), reader.ReadInt16());
    }

    public sealed class NeedsArgCodec(IMessageFieldAttribute attribute, int arg) : MessageFieldCodec(attribute)
    {
        public override void Encode(BinaryWriter writer, object value, Type sourceType) => writer.Write(arg);

        public override object Decode(BinaryReader reader, Type targetType) => reader.ReadInt32();
    }

    public sealed class ListsRequest : IMessage
    {
        public static Enum Command => CollectionCmd.ListsRequest;

        [CollectionMessageField(0, TypeCode.Byte)]
        public List<int> Numbers { get; set; } = [];

        [CollectionMessageField(1, TypeCode.UInt16)]
        public ushort[] Codes { get; set; } = [];

        [CollectionMessageField(2, TypeCode.Byte)]
        public IReadOnlyList<Color> Colors { get; set; } = [];

        [CollectionMessageField(3, TypeCode.Byte)]
        public List<Tag> Tags { get; set; } = [];

        [CollectionMessageField(4)]
        public byte[] Rest { get; set; } = [];
    }

    public sealed class FixedRequest : IMessage
    {
        public static Enum Command => CollectionCmd.FixedRequest;

        [CollectionMessageField(0, minCount: 3, maxCount: 3)]
        public int[] Values { get; set; } = [];

        [MessageField(1)]
        public byte Marker { get; set; }
    }

    public sealed class BoundedRequest : IMessage
    {
        public static Enum Command => CollectionCmd.BoundedRequest;

        [CollectionMessageField(0, TypeCode.Byte, minCount: 2, maxCount: 3)]
        public List<byte> Values { get; set; } = [];

        [MessageField(1)]
        public byte Marker { get; set; }
    }

    public sealed class SignedRequest : IMessage
    {
        public static Enum Command => CollectionCmd.SignedRequest;

        [CollectionMessageField(0, TypeCode.SByte)]
        public List<int> Values { get; set; } = [];
    }

    public sealed class EquipmentRequest : IMessage
    {
        public static Enum Command => CollectionCmd.EquipmentRequest;

        [CollectionMessageField(0)]
        public Dictionary<Slot, int> Slots { get; set; } = [];
    }

    public sealed class IndexedRequest : IMessage
    {
        public static Enum Command => CollectionCmd.IndexedRequest;

        [CollectionMessageField(0, TypeCode.Byte)]
        public Dictionary<int, ushort> Values { get; set; } = [];
    }

    public sealed class ElementCodecRequest : IMessage
    {
        public static Enum Command => CollectionCmd.ElementCodecRequest;

        [CollectionMessageField<MessageFieldCodec<byte>>(0, TypeCode.Byte)]
        public List<int> Small { get; set; } = [];

        [CollectionMessageField<PointCodec>(1, TypeCode.Byte)]
        public List<Point> Points { get; set; } = [];
    }

    public sealed class BadElementRequest : IMessage
    {
        public static Enum Command => CollectionCmd.BadElementRequest;

        [CollectionMessageField<NeedsArgCodec>(0, TypeCode.Byte)]
        public List<int> Values { get; set; } = [];
    }

    public sealed class UnknownRequest : IMessage
    {
        public static Enum Command => CollectionCmd.UnknownRequest;

        [CollectionMessageField(0, TypeCode.Byte)]
        public List<Point> Points { get; set; } = [];
    }

    public sealed class BlobRequest : IMessage
    {
        public static Enum Command => CollectionCmd.BlobRequest;

        [MessageField(0)]
        public byte Head { get; set; }

        [MessageField(1)]
        public byte[] Blob { get; set; } = [];
    }

    public sealed class CharsRequest : IMessage
    {
        public static Enum Command => CollectionCmd.CharsRequest;

        [CollectionMessageField(0)]
        public char[] Chars { get; set; } = [];
    }

    public sealed class BoolsRequest : IMessage
    {
        public static Enum Command => CollectionCmd.BoolsRequest;

        [CollectionMessageField(0)]
        public bool[] Flags { get; set; } = [];
    }

    private static T RoundTrip<T>(T message)
        where T : class, IMessage, new()
    {
        var codec = new DefaultMessageCodec();
        codec.Register<T>();

        return codec.Decode<T>(codec.Encode(message));
    }

    [Fact]
    public void Prefixed_WritesCountThenElements()
    {
        var codec = new DefaultMessageCodec();
        codec.Register<ListsRequest>();

        byte[] bytes = codec.Encode(new ListsRequest { Numbers = [1, 2], Codes = [7] });

        Assert.Equal(Convert.FromHexString("0102" + "02" + "01000000" + "02000000" + "0100" + "0700" + "00" + "00"), bytes);
    }

    [Fact]
    public void RoundTrip_ListsArraysEnumsSubMessagesAndRemainingBytes()
    {
        var original = new ListsRequest
        {
            Numbers = [1, -2, 3],
            Codes   = [10, 20],
            Colors  = [Color.Blue, Color.Red],
            Tags    = [new Tag { Name = "a" }, new Tag { Name = "bc" }],
            Rest    = [9, 8, 7],
        };

        var decoded = RoundTrip(original);

        Assert.Equal(original.Numbers, decoded.Numbers);
        Assert.Equal(original.Codes, decoded.Codes);
        Assert.Equal(original.Colors, decoded.Colors);
        Assert.Equal(["a", "bc"], decoded.Tags.Select(t => t.Name));
        Assert.Equal(original.Rest, decoded.Rest);
    }

    [Fact]
    public void FixedCount_PadsAndTruncates()
    {
        var codec = new DefaultMessageCodec();

        Assert.Equal(
            Convert.FromHexString("0202" + "01000000" + "00000000" + "00000000" + "AA"),
            codec.Encode(new FixedRequest { Values = [1], Marker = 0xAA }));

        byte[] truncated = codec.Encode(new FixedRequest { Values = [1, 2, 3, 4, 5], Marker = 0xAA });
        var decoded = codec.Decode<FixedRequest>(truncated);

        Assert.Equal([1, 2, 3], decoded.Values);
        Assert.Equal(0xAA, decoded.Marker);
    }

    [Fact]
    public void Bounded_ClampsCountOnEncode()
    {
        var codec = new DefaultMessageCodec();

        Assert.Equal(Convert.FromHexString("0302" + "02" + "0900" + "AA"),
            codec.Encode(new BoundedRequest { Values = [9], Marker = 0xAA }));

        Assert.Equal(Convert.FromHexString("0302" + "03" + "010203" + "AA"),
            codec.Encode(new BoundedRequest { Values = [1, 2, 3, 4, 5], Marker = 0xAA }));
    }

    [Fact]
    public void Bounded_ConsumesDeclaredElementsBeyondMaxToStayAligned()
    {
        var decoded = new DefaultMessageCodec().Decode<BoundedRequest>(
            Convert.FromHexString("0302" + "05" + "0102030405" + "AA"));

        Assert.Equal([1, 2, 3], decoded.Values);
        Assert.Equal(0xAA, decoded.Marker);
    }

    [Fact]
    public void Bounded_PadsDeclaredElementsBelowMin()
    {
        var decoded = new DefaultMessageCodec().Decode<BoundedRequest>(Convert.FromHexString("0302" + "01" + "07" + "AA"));

        Assert.Equal([7, 0], decoded.Values);
        Assert.Equal(0xAA, decoded.Marker);
    }

    [Fact]
    public void NegativeCount_Throws()
    {
        Assert.Throws<FormatException>(() =>
            new DefaultMessageCodec().Decode<SignedRequest>(Convert.FromHexString("0402FF")));
    }

    [Fact]
    public void EnumKeyedDictionary_IsWrittenAsValuesOrderedByKey()
    {
        var codec    = new DefaultMessageCodec();
        var original = new EquipmentRequest { Slots = new() { [Slot.Tail] = 3, [Slot.Head] = 1, [Slot.Body] = 2 } };

        byte[] bytes = codec.Encode(original);

        Assert.Equal(Convert.FromHexString("0502" + "01000000" + "02000000" + "03000000"), bytes);
        Assert.Equal(original.Slots, codec.Decode<EquipmentRequest>(bytes).Slots);
    }

    [Fact]
    public void NonEnumKeyedDictionary_IsRekeyedFromOne()
    {
        var codec = new DefaultMessageCodec();

        byte[] bytes = codec.Encode(new IndexedRequest { Values = new() { [20] = 7, [10] = 5 } });

        Assert.Equal(Convert.FromHexString("0602" + "02" + "0500" + "0700"), bytes);
        Assert.Equal(new Dictionary<int, ushort> { [1] = 5, [2] = 7 }, codec.Decode<IndexedRequest>(bytes).Values);
    }

    [Fact]
    public void ElementCodec_EncodesEachElement()
    {
        var codec    = new DefaultMessageCodec();
        var original = new ElementCodecRequest { Small = [1, 2, 3], Points = [new Point(1, -1)] };

        byte[] bytes = codec.Encode(original);

        Assert.Equal(Convert.FromHexString("0702" + "03010203" + "01" + "0100FFFF"), bytes);

        var decoded = codec.Decode<ElementCodecRequest>(bytes);
        Assert.Equal(original.Small, decoded.Small);
        Assert.Equal(original.Points, decoded.Points);
    }

    [Fact]
    public void ElementCodec_WithMismatchedArguments_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new DefaultMessageCodec().Encode(new BadElementRequest { Values = [1] }));
    }

    [Fact]
    public void UnsupportedElementType_Throws()
    {
        Assert.Throws<ArgumentException>(() => new DefaultMessageCodec().Encode(new UnknownRequest()));
    }

    [Fact]
    public void ByteArrayWithoutAttribute_ReadsRemainingBytes()
    {
        var codec = new DefaultMessageCodec();

        byte[] bytes = codec.Encode(new BlobRequest { Head = 1, Blob = [4, 5, 6] });

        Assert.Equal(Convert.FromHexString("0A0201040506"), bytes);
        Assert.Equal([4, 5, 6], codec.Decode<BlobRequest>(bytes).Blob);
    }

    [Fact]
    public void BoolArrayWithoutPrefix_ReadsRemainingBytes()
    {
        Assert.Equal([true, false, true], RoundTrip(new BoolsRequest { Flags = [true, false, true] }).Flags);
    }

    [Fact]
    public void CharArrayWithoutPrefix_ReadsRemainingBytes()
    {
        Assert.Equal(['h', 'i'], RoundTrip(new CharsRequest { Chars = ['h', 'i'] }).Chars);
    }

    [Fact]
    public void Constructor_WrapsPlainMessageFieldAttribute()
    {
        var codec = new CollectionMessageFieldCodec(new DefaultMessageCodec(), new MessageFieldAttribute(4));

        var attribute = Assert.IsType<CollectionMessageFieldAttribute>(codec.Attribute);
        Assert.Equal(4, attribute.Order);
        Assert.Equal(TypeCode.Empty, attribute.PrefixSizeType);
    }

    [Fact]
    public void Constructor_RejectsOtherAttributes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CollectionMessageFieldCodec(new DefaultMessageCodec(), new StringMessageFieldAttribute(0)));
    }
}
