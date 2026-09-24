using Encore.Messaging;

namespace Encore.Benchmarks;

public enum Cmd : ushort
{
    PingRequest      = 0x0001,
    PingResponse     = 0x0002,
    EchoRequest      = 0x0010,
    EchoResponse     = 0x0011,
    InventoryRequest = 0x0020,
    NotifyRequest    = 0x0030,
    ErrorResponse    = 0x00FF,
}

public sealed class PingRequest : IMessage
{
    public static Enum Command => Cmd.PingRequest;

    [MessageField(0)]
    public int Value { get; set; }
}

public sealed class PingResponse : IMessage
{
    public static Enum Command => Cmd.PingResponse;

    [MessageField(0)]
    public int Value { get; set; }
}

public sealed class EchoRequest : IMessage
{
    public static Enum Command => Cmd.EchoRequest;

    [StringMessageField(0, nullTerminated: false, prefixSizeType: TypeCode.UInt16)]
    public string Text { get; set; } = string.Empty;
}

public sealed class EchoResponse : IMessage
{
    public static Enum Command => Cmd.EchoResponse;

    [StringMessageField(0, nullTerminated: false, prefixSizeType: TypeCode.UInt16)]
    public string Text { get; set; } = string.Empty;
}

public sealed class ErrorResponse : IMessage
{
    public static Enum Command => Cmd.ErrorResponse;

    [StringMessageField(0)]
    public string Reason { get; set; } = string.Empty;
}

public enum Rarity : byte
{
    Common,
    Rare,
    Epic,
}

public sealed class Item : SubMessage
{
    [MessageField(0)]
    public uint Id { get; set; }

    [MessageField(1)]
    public Rarity Rarity { get; set; }

    [MessageField(2)]
    public short Quantity { get; set; }

    [StringMessageField(3)]
    public string Name { get; set; } = string.Empty;
}

public sealed class InventoryRequest : IMessage
{
    public static Enum Command => Cmd.InventoryRequest;

    [MessageField(0)]
    public Guid Owner { get; set; }

    [MessageField(1)]
    public DateTime UpdatedAt { get; set; }

    [CollectionMessageField(2, TypeCode.Byte)]
    public List<Item> Items { get; set; } = [];

    [CollectionMessageField(3, TypeCode.UInt16)]
    public int[] Slots { get; set; } = [];

    public static InventoryRequest Create(int items) => new()
    {
        Owner     = Guid.NewGuid(),
        UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        Items     = Enumerable.Range(0, items)
            .Select(i => new Item { Id = (uint)i, Rarity = (Rarity)(i % 3), Quantity = (short)i, Name = $"item-{i}" })
            .ToList(),
        Slots     = Enumerable.Range(0, items).ToArray(),
    };
}
