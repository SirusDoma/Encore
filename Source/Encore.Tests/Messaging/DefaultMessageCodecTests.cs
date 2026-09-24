using Encore.Messaging;

namespace Encore.Tests.Messaging;

public class DefaultMessageCodecTests
{
    public enum CodecCmd : ushort
    {
        PrimitivesRequest  = 0x0101,
        LayoutRequest      = 0x0102,
        OptionalRequest    = 0x0103,
        InventoryRequest   = 0x0104,
        DrawingRequest     = 0x0105,
        CustomRequest      = 0x0106,
        BadCodecRequest    = 0x0107,
        UnsupportedRequest = 0x0108,
        RestRequest        = 0x0109,
        EnumsRequest       = 0x010A,
        ComputedRequest    = 0x010B,
    }

    public enum I8Enum : sbyte { Value = -2 }
    public enum I16Enum : short { Value = -3 }
    public enum U16Enum : ushort { Value = 0xBEEF }
    public enum I32Enum { Value = -4 }
    public enum U32Enum : uint { Value = 0xDEADBEEF }
    public enum U64Enum : ulong { Value = ulong.MaxValue }

    public sealed class EnumsRequest : IMessage
    {
        public static Enum Command => CodecCmd.EnumsRequest;

        [MessageField(0)] public I8Enum I8 { get; set; }
        [MessageField(1)] public I16Enum I16 { get; set; }
        [MessageField(2)] public U16Enum U16 { get; set; }
        [MessageField(3)] public I32Enum I32 { get; set; }
        [MessageField(4)] public U32Enum U32 { get; set; }
        [MessageField(5)] public U64Enum U64 { get; set; }

        [MessageField<MessageFieldCodec<byte>>(6)]
        public I32Enum Narrow { get; set; }
    }

    public sealed class ComputedRequest : IMessage
    {
        public static Enum Command => CodecCmd.ComputedRequest;

        [MessageField(0)]
        public int Value { get; set; }

        [MessageField(1)]
        public int Doubled => Value * 2;
    }

    public enum Color : byte
    {
        Red   = 1,
        Green = 2,
        Blue  = 3,
    }

    public enum Huge : long
    {
        Low  = long.MinValue,
        High = long.MaxValue,
    }

    public sealed class PrimitivesRequest : IMessage
    {
        public static Enum Command => CodecCmd.PrimitivesRequest;

        [MessageField(0)]  public bool B { get; set; }
        [MessageField(1)]  public char C { get; set; }
        [MessageField(2)]  public byte U8 { get; set; }
        [MessageField(3)]  public sbyte I8 { get; set; }
        [MessageField(4)]  public short I16 { get; set; }
        [MessageField(5)]  public ushort U16 { get; set; }
        [MessageField(6)]  public int I32 { get; set; }
        [MessageField(7)]  public uint U32 { get; set; }
        [MessageField(8)]  public long I64 { get; set; }
        [MessageField(9)]  public ulong U64 { get; set; }
        [MessageField(10)] public float F32 { get; set; }
        [MessageField(11)] public double F64 { get; set; }
        [MessageField(12)] public decimal Dec { get; set; }
        [MessageField(13)] public DateTime Date { get; set; }
        [MessageField(14)] public TimeSpan Span { get; set; }
        [MessageField(15)] public Guid Id { get; set; }
        [MessageField(16)] public Color Color { get; set; }
        [MessageField(17)] public Huge Huge { get; set; }
        [MessageField(18)] public string Tail { get; set; } = string.Empty;
    }

    public sealed class LayoutRequest : IMessage
    {
        public static Enum Command => CodecCmd.LayoutRequest;

        [MessageField(2)]
        public byte Third;

        [MessageField(0)]
        public ushort First { get; set; }

        [MessageField(1)]
        private int _second;

        public int Second
        {
            get => _second;
            set => _second = value;
        }

        public int Ignored { get; set; }
    }

    public sealed class OptionalRequest : IMessage
    {
        public static Enum Command => CodecCmd.OptionalRequest;

        [MessageField(0)]
        public int Required { get; set; }

        [MessageField(1)]
        public int? Count { get; set; }

        [StringMessageField(2)]
        public string? Note { get; set; }
    }

    public sealed class Item : SubMessage
    {
        [MessageField(0)]
        public ushort Id { get; set; }

        [StringMessageField(1)]
        public string Name { get; set; } = string.Empty;
    }

    public sealed class InventoryRequest : IMessage
    {
        public static Enum Command => CodecCmd.InventoryRequest;

        [MessageField(0)]
        public Item Main { get; set; } = new();

        [CollectionMessageField(1, TypeCode.Byte)]
        public List<Item> Items { get; set; } = [];
    }

    public abstract class Shape : SubMessage;

    public sealed class Circle : Shape
    {
        [MessageField(0)]
        public int Radius { get; set; }
    }

    public sealed class DrawingRequest : IMessage
    {
        public static Enum Command => CodecCmd.DrawingRequest;

        [MessageField(0)]
        public Shape? Shape { get; set; }
    }

    public sealed class CircleCodec(IMessageFieldAttribute attribute) : MessageFieldCodec(attribute)
    {
        public override void Encode(BinaryWriter writer, object value, Type sourceType) =>
            writer.Write(((Circle)value).Radius);

        public override object Decode(BinaryReader reader, Type targetType) =>
            new Circle { Radius = reader.ReadInt32() };
    }

    public sealed class CodedDrawingRequest : IMessage
    {
        public static Enum Command => CodecCmd.DrawingRequest;

        [MessageField<CircleCodec>(0)]
        public Shape? Shape { get; set; }
    }

    public sealed class ScaledCodec(IMessageFieldAttribute attribute, int scale) : MessageFieldCodec(attribute)
    {
        public override void Encode(BinaryWriter writer, object value, Type sourceType) =>
            writer.Write((int)Math.Round((double)value * scale));

        public override object Decode(BinaryReader reader, Type targetType) =>
            reader.ReadInt32() / (double)scale;
    }

    public sealed class CustomRequest : IMessage
    {
        public static Enum Command => CodecCmd.CustomRequest;

        [MessageField<MessageFieldCodec<short>>(0)]
        public int Narrow { get; set; }

        [MessageField<ScaledCodec>(1, 100)]
        public double Price { get; set; }

        [MessageField(2, typeof(ScaledCodec), 10)]
        public double Weight { get; set; }
    }

    public sealed class BadCodecRequest : IMessage
    {
        public static Enum Command => CodecCmd.BadCodecRequest;

        [MessageField<ScaledCodec>(0)]
        public double Price { get; set; }
    }

    public sealed class UnsupportedRequest : IMessage
    {
        public static Enum Command => CodecCmd.UnsupportedRequest;

        [MessageField(0)]
        public Version Version { get; set; } = new(1, 0);
    }

    public sealed class RestRequest : IMessage
    {
        public static Enum Command => CodecCmd.RestRequest;

        [MessageField(0)]
        public byte Head { get; set; }

        [MessageField(1)]
        public string Text { get; set; } = string.Empty;
    }

    public sealed class Unbound
    {
        [MessageField(0)]
        public int Value { get; set; }
    }

    [Fact]
    public void Encode_WritesCommandThenMembersInOrder()
    {
        var codec   = new DefaultMessageCodec();
        var message = new LayoutRequest { First = 0x0A0B, Second = 1, Third = 0xFF, Ignored = 99 };

        Assert.Equal(Convert.FromHexString("02010B0A01000000FF"), codec.Encode(message));
    }

    [Fact]
    public void Decode_ReadsPublicAndPrivateMembersAndSkipsUnattributed()
    {
        var codec   = new DefaultMessageCodec();
        var decoded = codec.Decode<LayoutRequest>(Convert.FromHexString("02010B0A01000000FF"));

        Assert.Equal(0x0A0B, decoded.First);
        Assert.Equal(1, decoded.Second);
        Assert.Equal(0xFF, decoded.Third);
        Assert.Equal(0, decoded.Ignored);
    }

    [Fact]
    public void RoundTrip_AllStandardTypes()
    {
        var codec    = new DefaultMessageCodec();
        var original = new PrimitivesRequest
        {
            B     = true,
            C     = 'Z',
            U8    = 0xFE,
            I8    = -5,
            I16   = -1234,
            U16   = 54321,
            I32   = int.MinValue,
            U32   = uint.MaxValue,
            I64   = long.MinValue,
            U64   = ulong.MaxValue,
            F32   = 3.5f,
            F64   = -2.25,
            Dec   = 12345.6789m,
            Date  = new DateTime(2024, 2, 29, 13, 37, 42, DateTimeKind.Utc),
            Span  = TimeSpan.FromMilliseconds(123456),
            Id    = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
            Color = Color.Blue,
            Huge  = Huge.Low,
            Tail  = "tail",
        };

        byte[] bytes = codec.Encode(original);

        Assert.Equal(2 + 102 + 4, bytes.Length);
        Assert.Equivalent(original, codec.Decode<PrimitivesRequest>(bytes), strict: true);
    }

    [Fact]
    public void Enums_UseUnderlyingTypeWidthUnlessCodecOverrides()
    {
        var codec    = new DefaultMessageCodec();
        var original = new EnumsRequest
        {
            I8     = I8Enum.Value,
            I16    = I16Enum.Value,
            U16    = U16Enum.Value,
            I32    = I32Enum.Value,
            U32    = U32Enum.Value,
            U64    = U64Enum.Value,
            Narrow = (I32Enum)7,
        };

        byte[] bytes = codec.Encode(original);

        Assert.Equal(2 + 1 + 2 + 2 + 4 + 4 + 8 + 1, bytes.Length);
        Assert.Equivalent(original, codec.Decode<EnumsRequest>(bytes), strict: true);
    }

    [Fact]
    public void GetOnlyMember_IsEncodedButNotDecoded()
    {
        var codec = new DefaultMessageCodec();

        byte[] bytes = codec.Encode(new ComputedRequest { Value = 3 });

        Assert.Equal(Convert.FromHexString("0B010300000006000000"), bytes);
        Assert.Equal(3, codec.Decode<ComputedRequest>(bytes).Value);
    }

    [Fact]
    public void PlainStringMember_IsNotTerminatedAndConsumesRemainingBytes()
    {
        var codec = new DefaultMessageCodec();
        byte[] bytes = codec.Encode(new RestRequest { Head = 7, Text = "hello" });

        Assert.Equal(Convert.FromHexString("09010768656C6C6F"), bytes);
        Assert.Equal("hello", codec.Decode<RestRequest>(bytes).Text);
    }

    [Fact]
    public void NullableMembers_AreOmittedWhenNullAndSkippedAtEndOfPayload()
    {
        var codec = new DefaultMessageCodec();
        byte[] bytes = codec.Encode(new OptionalRequest { Required = 5 });

        Assert.Equal(Convert.FromHexString("030105000000"), bytes);

        var decoded = codec.Decode<OptionalRequest>(bytes);
        Assert.Equal(5, decoded.Required);
        Assert.Null(decoded.Count);
        Assert.Null(decoded.Note);
    }

    [Fact]
    public void NullableMembers_RoundTripWhenPresent()
    {
        var codec   = new DefaultMessageCodec();
        var decoded = codec.Decode<OptionalRequest>(
            codec.Encode(new OptionalRequest { Required = 1, Count = 2, Note = "n" }));

        Assert.Equal(1, decoded.Required);
        Assert.Equal(2, decoded.Count);
        Assert.Equal("n", decoded.Note);
    }

    [Fact]
    public void Decode_TruncatedRequiredMember_Throws()
    {
        var codec = new DefaultMessageCodec();

        Assert.ThrowsAny<ArgumentException>(() => codec.Decode<PingRequest>(Convert.FromHexString("010002")));
    }

    [Fact]
    public void RoundTrip_NestedSubMessagesAndCollections()
    {
        var codec = new DefaultMessageCodec();
        codec.Register<InventoryRequest>();

        var original = new InventoryRequest
        {
            Main  = new Item { Id = 1, Name = "sword" },
            Items = [new Item { Id = 2, Name = "shield" }, new Item { Id = 3, Name = "potion" }],
        };

        var decoded = Assert.IsType<InventoryRequest>(codec.Decode(codec.Encode((IMessage)original)));

        Assert.Equivalent(original, decoded, strict: true);
    }

    [Fact]
    public void Register_RegistersNestedMessageTypes()
    {
        var codec = new DefaultMessageCodec();
        codec.Register<InventoryRequest>();

        Assert.Equal(typeof(InventoryRequest), codec.GetRegisteredType(typeof(InventoryRequest)));
        Assert.Equal(typeof(Item), codec.GetRegisteredType(typeof(Item)));
    }

    [Fact]
    public void Encode_CollectionOfUnregisteredSubMessages_Throws()
    {
        var codec = new DefaultMessageCodec();

        Assert.Throws<ArgumentException>(() => codec.Encode(new InventoryRequest { Items = [new Item()] }));
    }

    [Fact]
    public void Encode_AbstractMember_UsesRuntimeType()
    {
        var codec = new DefaultMessageCodec();

        Assert.Equal(
            Convert.FromHexString("050105000000"),
            codec.Encode(new DrawingRequest { Shape = new Circle { Radius = 5 } }));
    }

    [Fact]
    public void Decode_AbstractMemberWithoutCodec_Throws()
    {
        var codec = new DefaultMessageCodec();

        Assert.Throws<InvalidOperationException>(() =>
            codec.Decode<DrawingRequest>(Convert.FromHexString("050105000000")));
    }

    [Fact]
    public void Decode_AbstractMemberWithCodec_UsesCodec()
    {
        var codec   = new DefaultMessageCodec();
        var decoded = codec.Decode<CodedDrawingRequest>(Convert.FromHexString("050105000000"));

        Assert.Equal(5, Assert.IsType<Circle>(decoded.Shape).Radius);
    }

    [Fact]
    public void CustomCodecs_ControlMemberEncoding()
    {
        var codec   = new DefaultMessageCodec();
        var message = new CustomRequest { Narrow = 7, Price = 1.25, Weight = 2.5 };

        byte[] bytes = codec.Encode(message);

        Assert.Equal(Convert.FromHexString("060107007D00000019000000"), bytes);
        Assert.Equivalent(message, codec.Decode<CustomRequest>(bytes), strict: true);
    }

    [Fact]
    public void CustomCodec_WithMismatchedArguments_Throws()
    {
        var codec = new DefaultMessageCodec();

        var ex = Assert.Throws<InvalidOperationException>(() => codec.Encode(new BadCodecRequest { Price = 1 }));
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void UnsupportedMemberType_Throws()
    {
        var codec = new DefaultMessageCodec();

        Assert.Throws<NotSupportedException>(() => codec.Encode(new UnsupportedRequest()));
        Assert.Throws<NotSupportedException>(() =>
            codec.Decode<UnsupportedRequest>(Convert.FromHexString("080100")));
    }

    [Fact]
    public void GetRegisteredType_ReturnsNullForUnknownOrNull()
    {
        var codec = new DefaultMessageCodec();

        Assert.Null(codec.GetRegisteredType(null));
        Assert.Null(codec.GetRegisteredType(typeof(PingRequest)));
    }

    [Fact]
    public void RegisterGeneric_IsIdempotentAndEnablesUntypedDecode()
    {
        var codec = new DefaultMessageCodec();
        codec.Register<PingRequest>();
        codec.Register<PingRequest>();

        var decoded = codec.Decode(Wire.Encode(new PingRequest { Value = 3 }));

        Assert.Equal(3, Assert.IsType<PingRequest>(decoded).Value);
        Assert.Equal(typeof(PingRequest), codec.GetRegisteredType(typeof(PingRequest)));
    }

    [Fact]
    public void RegisterGeneric_ConflictingTypeForSameCommand_Throws()
    {
        var codec = new DefaultMessageCodec();
        codec.Register<PingRequest>();

        Assert.Throws<InvalidOperationException>(() => codec.Register<AltPingRequest>());
    }

    [Fact]
    public void RegisterType_BehavesLikeGenericRegister()
    {
        var codec = new DefaultMessageCodec();
        codec.Register(typeof(PingRequest));
        codec.Register(typeof(PingRequest));

        Assert.IsType<PingRequest>(codec.Decode(Wire.Encode(new PingRequest())));
        Assert.Throws<InvalidOperationException>(() => codec.Register(typeof(AltPingRequest)));
    }

    [Fact]
    public void RegisterType_RejectsNonMessagesInterfacesAndSubMessages()
    {
        var codec = new DefaultMessageCodec();

        Assert.Throws<ArgumentOutOfRangeException>(() => codec.Register(typeof(Unbound)));
        Assert.Throws<ArgumentOutOfRangeException>(() => codec.Register(typeof(IMessage)));
        Assert.Throws<InvalidOperationException>(() => codec.Register(typeof(Item)));
    }

    [Fact]
    public void RegisterCommandWithType_BindsTypeToCommand()
    {
        var codec = new DefaultMessageCodec();
        codec.Register(Cmd.PingRequest, typeof(PingRequest));
        codec.Register(Cmd.PingRequest, typeof(PingRequest));

        Assert.IsType<PingRequest>(codec.Decode(Wire.Encode(new PingRequest())));
        Assert.Throws<InvalidOperationException>(() => codec.Register(Cmd.PingRequest, typeof(AltPingRequest)));
        Assert.Throws<ArgumentException>(() => codec.Register(Cmd.NotifyRequest, typeof(Unbound)));
    }

    [Fact]
    public void RegisterCommand_DecodesToNullMessageAndKnownCommand()
    {
        var codec = new DefaultMessageCodec();
        codec.Register(Cmd.LogoutRequest);
        codec.Register(Cmd.LogoutRequest);

        byte[] payload = Convert.FromHexString("3000");

        Assert.Null(codec.Decode(payload));
        Assert.Equal<Enum>(Cmd.LogoutRequest, codec.DecodeCommand(payload));
    }

    [Fact]
    public void RegisterCommand_ConflictingEnumForSameCode_Throws()
    {
        var codec = new DefaultMessageCodec();
        codec.Register(Cmd.PingRequest);

        Assert.Throws<InvalidOperationException>(() => codec.Register(OtherCmd.Clash));
    }

    [Fact]
    public void EncodeUntyped_RequiresRegistrationAndMatchesTypedEncoding()
    {
        var codec = new DefaultMessageCodec();
        var ping  = new PingRequest { Value = 9 };

        Assert.Throws<NotSupportedException>(() => codec.Encode((IMessage)ping));

        codec.Register<PingRequest>();
        Assert.Equal(codec.Encode(ping), codec.Encode((IMessage)ping));
    }

    [Fact]
    public void Encode_Null_Throws()
    {
        var codec = new DefaultMessageCodec();

        Assert.Throws<ArgumentNullException>(() => codec.Encode<PingRequest>(null!));
        Assert.Throws<ArgumentNullException>(() => codec.Encode((IMessage)null!));
    }

    [Fact]
    public void EncodeCommand_WritesLittleEndianCode()
    {
        var codec = new DefaultMessageCodec();

        Assert.Equal(Convert.FromHexString("3000"), codec.EncodeCommand(Cmd.LogoutRequest));

        codec.Register(Cmd.LogoutResponse);
        Assert.Equal(Convert.FromHexString("3100"), codec.EncodeCommand(Cmd.LogoutResponse));
    }

    [Fact]
    public void DecodeTyped_CommandMismatch_Throws()
    {
        var codec = new DefaultMessageCodec();

        Assert.Throws<InvalidOperationException>(() => codec.Decode<PingRequest>(Wire.Encode(new PingResponse())));
        Assert.Throws<ArgumentNullException>(() => codec.Decode<PingRequest>(null!));
    }

    [Fact]
    public void DecodeUntyped_UnknownCommand_Throws()
    {
        var codec = new DefaultMessageCodec();

        Assert.Throws<NotSupportedException>(() => codec.Decode(Convert.FromHexString("9999")));
        Assert.Throws<ArgumentNullException>(() => codec.Decode(null!));
    }

    [Fact]
    public void DecodeCommand_ResolvesFromRegisteredTypeOrThrows()
    {
        var codec = new DefaultMessageCodec();
        codec.Register<PingResponse>();

        Assert.Equal<Enum>(Cmd.PingResponse, codec.DecodeCommand(Wire.Encode(new PingResponse())));
        Assert.Throws<NotSupportedException>(() => codec.DecodeCommand(Convert.FromHexString("9999")));
    }

    [Fact]
    public void AsFieldCodec_EncodesAndDecodesValuesByType()
    {
        IMessageFieldCodec codec = new DefaultMessageCodec();

        using var stream = new MemoryStream();
        codec.Encode(new BinaryWriter(stream), Color.Green, typeof(Color));
        codec.Encode(new BinaryWriter(stream), 0x01020304, typeof(int));

        Assert.Equal(Convert.FromHexString("0204030201"), stream.ToArray());

        stream.Position = 0;
        var reader = new BinaryReader(stream);
        Assert.Equal(Color.Green, codec.Decode(reader, typeof(Color)));
        Assert.Equal(0x01020304, codec.Decode(reader, typeof(int)));
    }

    [Fact]
    public void GenericCodec_UsesCommandTypeWidth()
    {
        var codec = new DefaultMessageCodec<uint>();
        codec.Register<WideRequest>();
        codec.Register(WideCmd.NotifyRequest);

        byte[] bytes = codec.Encode(new WideRequest { Value = 7 });

        Assert.Equal(Convert.FromHexString("0100010007000000"), bytes);
        Assert.Equal(bytes, codec.Encode((IMessage)new WideRequest { Value = 7 }));
        Assert.Equal(7, codec.Decode<WideRequest>(bytes).Value);
        Assert.Equal(7, Assert.IsType<WideRequest>(codec.Decode(bytes)).Value);
        Assert.Equal<Enum>(WideCmd.WideRequest, codec.DecodeCommand(bytes));

        Assert.Equal(Convert.FromHexString("20000100"), codec.EncodeCommand(WideCmd.NotifyRequest));
        Assert.Equal<Enum>(WideCmd.NotifyRequest, codec.DecodeCommand(Convert.FromHexString("20000100")));
        Assert.Throws<FormatException>(() => codec.DecodeCommand(Convert.FromHexString("2000")));

        var ex = Assert.Throws<NotSupportedException>(() => codec.Decode(Convert.FromHexString("50000100")));
        Assert.Equal("Command '0x00010050' is not recognized", ex.Message);
    }

    [Fact]
    public void GenericCodec_CommandIndependentOfEnumUnderlyingType()
    {
        Assert.Equal(Convert.FromHexString("0107000000"), new DefaultMessageCodec<byte>().Encode(new PingRequest { Value = 7 }));
        Assert.Equal(Convert.FromHexString("0100000007000000"), new DefaultMessageCodec<uint>().Encode(new PingRequest { Value = 7 }));
    }

    [Fact]
    public void GenericCodec_CommandOutOfRange_Throws()
    {
        var codec = new DefaultMessageCodec<byte>();

        var ex = Assert.Throws<OverflowException>(() => codec.Register<LayoutRequest>());
        Assert.IsType<OverflowException>(ex.InnerException);

        Assert.Throws<OverflowException>(() => codec.Encode(new LayoutRequest()));
        Assert.Throws<OverflowException>(() => codec.EncodeCommand(CodecCmd.LayoutRequest));
        Assert.Throws<OverflowException>(() => codec.Decode<LayoutRequest>(Convert.FromHexString("02")));
        Assert.Throws<OverflowException>(() => new DefaultMessageCodec().Encode(new WideRequest()));
    }

    [Fact]
    public void SubMessage_HasNoCommand()
    {
        Assert.Throws<NotSupportedException>(() => SubMessage.Command);
    }
}
