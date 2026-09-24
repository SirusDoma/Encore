using Encore.Messaging;

namespace Encore.Tests.Fixtures;

public enum Cmd : ushort
{
    PingRequest       = 0x0001,
    PingResponse      = 0x0002,
    EchoRequest       = 0x0010,
    EchoResponse      = 0x0011,
    NotifyRequest     = 0x0020,
    LogoutRequest     = 0x0030,
    LogoutResponse    = 0x0031,
    CrashRequest      = 0x0040,
    CrashAsyncRequest = 0x0041,
    UnmappedRequest   = 0x0050,
    FailureResponse   = 0x00FF,
}

public enum OtherCmd : ushort
{
    Clash = 0x0001,
}

public enum WideCmd : uint
{
    WideRequest     = 0x00010001,
    NotifyRequest   = 0x00010020,
    UnmappedRequest = 0x00010050,
}

public enum SignedCmd
{
    UnmappedRequest = -1,
}

public sealed class WideRequest : IMessage
{
    public static Enum Command => WideCmd.WideRequest;

    [MessageField(0)]
    public int Value { get; set; }
}

public sealed class PingRequest : IMessage
{
    public static Enum Command => Cmd.PingRequest;

    [MessageField(0)]
    public int Value { get; set; }
}

public sealed class AltPingRequest : IMessage
{
    public static Enum Command => Cmd.PingRequest;

    [MessageField(0)]
    public short Value { get; set; }
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

    [StringMessageField(0)]
    public string Text { get; set; } = string.Empty;
}

public sealed class EchoResponse : IMessage
{
    public static Enum Command => Cmd.EchoResponse;

    [StringMessageField(0)]
    public string Text { get; set; } = string.Empty;
}

public sealed class FailureResponse : IMessage
{
    public static Enum Command => Cmd.FailureResponse;

    [StringMessageField(0)]
    public string Reason { get; set; } = string.Empty;
}
