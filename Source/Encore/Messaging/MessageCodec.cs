using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Encore.Messaging;

public interface IMessageCodec
{
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    Type? GetRegisteredType(Type? type);

    void Register<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>()
        where T : class, IMessage;

    void Register<T>(T command)
        where T : Enum;

    void Register<T>(T command, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
        where T : Enum;

    void Register([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type);

    byte[] Encode<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T message)
        where T : class, IMessage;

    byte[] Encode(IMessage message);

    byte[] EncodeCommand(Enum command);

    T Decode<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(byte[] data)
        where T : class, IMessage, new();

    IMessage? Decode(byte[] data);

    Enum DecodeCommand(byte[] data);
}

file static class StandardFieldCodecs
{
    public static readonly Dictionary<Type, Func<IMessageFieldAttribute, MessageFieldCodec>> Factories = new()
    {
        { typeof(bool),     (attribute) => new MessageFieldCodec<bool>(attribute)     },
        { typeof(char),     (attribute) => new MessageFieldCodec<char>(attribute)     },
        { typeof(byte),     (attribute) => new MessageFieldCodec<byte>(attribute)     },
        { typeof(sbyte),    (attribute) => new MessageFieldCodec<sbyte>(attribute)    },
        { typeof(short),    (attribute) => new MessageFieldCodec<short>(attribute)    },
        { typeof(ushort),   (attribute) => new MessageFieldCodec<ushort>(attribute)   },
        { typeof(int),      (attribute) => new MessageFieldCodec<int>(attribute)      },
        { typeof(uint),     (attribute) => new MessageFieldCodec<uint>(attribute)     },
        { typeof(long),     (attribute) => new MessageFieldCodec<long>(attribute)     },
        { typeof(ulong),    (attribute) => new MessageFieldCodec<ulong>(attribute)    },
        { typeof(float),    (attribute) => new MessageFieldCodec<float>(attribute)    },
        { typeof(double),   (attribute) => new MessageFieldCodec<double>(attribute)   },
        { typeof(decimal),  (attribute) => new MessageFieldCodec<decimal>(attribute)  },
        { typeof(string),   (attribute) => new StringMessageFieldCodec(attribute)     },
        { typeof(DateTime), (attribute) => new MessageFieldCodec<DateTime>(attribute) },
        { typeof(TimeSpan), (attribute) => new MessageFieldCodec<TimeSpan>(attribute) },
        { typeof(Guid),     (attribute) => new MessageFieldCodec<Guid>(attribute)     }
    };
}

public class DefaultMessageCodec : DefaultMessageCodec<ushort>;

public partial class DefaultMessageCodec<TCommandCode> : IMessageCodec, IMessageFieldCodec
    where TCommandCode : unmanaged, IBinaryInteger<TCommandCode>, IConvertible
{
    private static readonly TypeCode CodeType = Type.GetTypeCode(typeof(TCommandCode));

    private static TCommandCode ToCommandCode(Enum command)
    {
        try
        {
            return (TCommandCode)Convert.ChangeType(command, CodeType);
        }
        catch (OverflowException ex)
        {
            throw new OverflowException(
                $"Command '{command}' (0x{command:X}) does not fit in {typeof(TCommandCode).Name}", ex
            );
        }
    }

    private static TCommandCode ReadCommandCode(BinaryReader reader)
    {
        int size = default(TCommandCode).GetByteCount();
        long remaining = reader.BaseStream.Length - reader.BaseStream.Position;
        if (size > remaining)
            throw new FormatException($"Command code length ({size} bytes) exceeds the remaining data ({remaining} bytes)");

        return (TCommandCode)reader.ReadInteger(CodeType);
    }

    private static string FormatCode(TCommandCode code) =>
        $"0x{code.ToString($"X{code.GetByteCount() * 2}", null)}";

    private readonly Dictionary<TCommandCode, Type> _types = new();
    private readonly Dictionary<TCommandCode, Enum> _commands = [];

    private readonly HashSet<Type> _typeSet = [];

    private readonly ConcurrentDictionary<Type, MessageMember[]> _members = new();
    private readonly ConcurrentDictionary<Type, Enum> _messageCommands = new();
    private readonly ConcurrentDictionary<(Type, IMessageFieldAttribute), IMessageFieldCodec> _valueCodecs =
        new(ValueCodecKeyComparer.Instance);

    private readonly MessageFieldAttribute _defaultAttribute = new(0);

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type? GetRegisteredType(Type? type) =>
        type != null && _typeSet.TryGetValue(type, out var v) ? v : null;

    public void Register<T>(T command) where T : Enum
    {
        var code = ToCommandCode(command);
        if (!_commands.TryAdd(code, command) && !Equals(command, _commands[code]))
            throw new InvalidOperationException($"'{FormatCode(code)}' is already bound to '{_commands[code]}'");
    }

    public void Register<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>()
        where T : class, IMessage
    {
        var type = typeof(T);
        var code = ToCommandCode(T.Command);

        if (!_types.TryAdd(code, type) && type != _types[code])
            throw new InvalidOperationException($"'{FormatCode(code)}' is already bound to '{_types[code].Name}'");

        RegisterType(type);
    }

    public void Register([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        if (!typeof(IMessage).IsAssignableFrom(type) || type.IsInterface)
            throw new ArgumentOutOfRangeException( nameof(type), $"Type '{type.Name}' must implement IMessage.");

        var code = ToCommandCode(GetCommand(type));
        if (!_types.TryAdd(code, type) && type != _types[code])
            throw new InvalidOperationException($"'{FormatCode(code)}' is already bound to '{_types[code].Name}'");

        RegisterType(type);
    }

    public void Register<T>(T command, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
        where T : Enum
    {
        if (!typeof(IMessage).IsAssignableFrom(type) || type.IsInterface)
            throw new ArgumentException($"Type '{type.Name}' must implement IMessage.", nameof(type));

        var code = ToCommandCode(command);
        if (!_types.TryAdd(code, type) && type != _types[code])
            throw new InvalidOperationException($"'{FormatCode(code)}' is already bound to '{_types[code].Name}'");

        RegisterType(type);
    }


    private void RegisterType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        if (!_typeSet.Add(type))
            return;

        foreach (var member in GetSerializableMembers(type))
        {
            var memberType = member.MemberType;
            if (typeof(IEnumerable).IsAssignableFrom(member.MemberType) && member.MemberType != typeof(string))
                memberType = GetEnumerableElementType(member.MemberType);

            if (memberType.IsAssignableTo(typeof(IMessage)))
                RegisterType(memberType);
        }
    }

    public byte[] Encode<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T message)
        where T : class, IMessage
    {
        ArgumentNullException.ThrowIfNull(message);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.WriteInteger(ToCommandCode(T.Command), CodeType);

        EncodeMessage(writer, message, typeof(T));

        return stream.ToArray();
    }

    public byte[] Encode(IMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        if (!_typeSet.TryGetValue(message.GetType(), out var type))
            throw new NotSupportedException($"'{message.GetType()}' is not recognized");

        writer.WriteInteger(ToCommandCode(GetCommand(type)), CodeType);

        EncodeMessage(writer, message, type);
        return stream.ToArray();
    }

    public byte[] EncodeCommand(Enum command)
    {
        var code = ToCommandCode(command);
        var bytes = new byte[code.GetByteCount()];
        code.WriteLittleEndian(bytes);

        if (_commands.TryGetValue(code, out var cmd))
            return bytes;

        //throw new NotSupportedException($"Command '{FormatCode(code)}' is not recognized");
        return bytes;
    }

    public T Decode<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(byte[] data)
        where T : class, IMessage, new()
    {
        ArgumentNullException.ThrowIfNull(data);

        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);

        var command  = ReadCommandCode(reader);
        var expected = ToCommandCode(T.Command);

        if (command != expected)
        {
            throw new InvalidOperationException($"Command mismatch. " +
                                                $"Expected {FormatCode(expected)} but got {FormatCode(command)}");
        }

        return (T)DecodeMessage(reader, typeof(T));
    }

    public IMessage? Decode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);

        var command = ReadCommandCode(reader);

        if (!_types.TryGetValue(command, out var messageType))
        {
            if (_commands.ContainsKey(command))
                return null;

            throw new NotSupportedException($"Command '{FormatCode(command)}' is not recognized");
        }

        return DecodeMessage(reader, messageType);
    }

    public Enum DecodeCommand(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);

        var command = ReadCommandCode(reader);

        if (_commands.TryGetValue(command, out var cmd))
            return cmd;

        if (_types.TryGetValue(command, out var type))
            return GetCommand(type);

        throw new NotSupportedException($"Command '{FormatCode(command)}' is not recognized");
    }

    private void EncodeMessage(BinaryWriter writer, IMessage message,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        foreach (var member in GetSerializableMembers(type))
        {
            if (!member.CanRead)
                continue;

            var value  = member.GetValue(message);
            var codec      = member.GetFieldCodec(this);
            var memberType = member.MemberType;
            var attribute  = member.Attribute;

            if (memberType.IsGenericType && memberType.GetGenericTypeDefinition() == typeof(Nullable<>))
                memberType = Nullable.GetUnderlyingType(memberType)!;

            if (typeof(IMessage).IsAssignableFrom(memberType) && memberType.IsAbstract && value != null)
                memberType = value.GetType();

            if (codec != null && value != null)
                codec.Encode(writer, value, memberType);
            else
                EncodeValue(writer, value, memberType, attribute);
        }
    }

    private IMessage DecodeMessage(BinaryReader reader,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type
    )
    {
        var message = (IMessage)Activator.CreateInstance(type)!;
        foreach (var member in GetSerializableMembers(type))
        {
            if (!member.CanWrite)
                continue;

            if (reader.BaseStream.Position == reader.BaseStream.Length && member.IsNullable)
                continue;

            object value;
            var codec      = member.GetFieldCodec(this);
            var memberType = member.MemberType;
            var attribute  = member.Attribute;

            if (memberType.IsGenericType && memberType.GetGenericTypeDefinition() == typeof(Nullable<>))
                memberType = Nullable.GetUnderlyingType(memberType)!;

            if (typeof(IMessage).IsAssignableFrom(memberType) && memberType.IsAbstract && codec == null)
                throw new InvalidOperationException("Cannot determine abstract type for decoding");

            if (codec != null)
                value = codec.Decode(reader, memberType);
            else
                value = DecodeValue(reader, memberType, attribute);

            if (!memberType.IsInstanceOfType(value))
            {
                if (memberType.IsEnum)
                    value = Enum.ToObject(memberType, Convert.ChangeType(value, Enum.GetUnderlyingType(memberType)));
                else
                    value = Convert.ChangeType(value, memberType);
            }

            member.SetValue(message, value);
        }

        return message;
    }

    private void EncodeValue(BinaryWriter writer, object? value,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type, IMessageFieldAttribute attribute)
    {
        // ArgumentNullException.ThrowIfNull(value);

        if (value == null)
            return;

        if (StandardFieldCodecs.Factories.ContainsKey(type))
        {
            GetValueCodec(type, attribute).Encode(writer, value, type);
            return;
        }

        if (type.IsEnum)
        {
            GetValueCodec(type, attribute).Encode(writer, Convert.ChangeType(value, Enum.GetUnderlyingType(type)), type);
            return;
        }

        if (typeof(IMessage).IsAssignableFrom(type))
        {
            EncodeMessage(writer, (IMessage)value, type);
            return;
        }

        if (typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string))
        {
            GetValueCodec(type, attribute).Encode(writer, value, type);
            return;
        }

        throw new NotSupportedException($"Type '{type.Name}' is not supported for encoding");
    }

    private object DecodeValue(BinaryReader reader,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type, IMessageFieldAttribute attribute)
    {
        if (StandardFieldCodecs.Factories.ContainsKey(type))
        {
            return GetValueCodec(type, attribute).Decode(reader, type);
        }

        if (type.IsEnum)
        {
            return Enum.ToObject(type, GetValueCodec(type, attribute).Decode(reader, type));
        }

        if (typeof(IMessage).IsAssignableFrom(type))
        {
            return DecodeMessage(reader, type);
        }

        if (typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string))
        {
            return GetValueCodec(type, attribute).Decode(reader, type);
        }

        throw new NotSupportedException($"Type '{type.Name}' is not supported for decoding");
    }

    private IMessageFieldCodec GetValueCodec(Type type, IMessageFieldAttribute attribute)
    {
        if (_valueCodecs.TryGetValue((type, attribute), out var codec))
            return codec;

        if (StandardFieldCodecs.Factories.TryGetValue(type, out var serializer))
        {
            codec = serializer(attribute);
        }
        else if (type.IsEnum)
        {
            codec = Type.GetTypeCode(Enum.GetUnderlyingType(type)) switch
            {
                TypeCode.Char   => new MessageFieldCodec<char>(attribute),
                TypeCode.SByte  => new MessageFieldCodec<sbyte>(attribute),
                TypeCode.Byte   => new MessageFieldCodec<byte>(attribute),
                TypeCode.Int16  => new MessageFieldCodec<short>(attribute),
                TypeCode.UInt16 => new MessageFieldCodec<ushort>(attribute),
                TypeCode.Int32  => new MessageFieldCodec<int>(attribute),
                TypeCode.UInt32 => new MessageFieldCodec<uint>(attribute),
                TypeCode.Int64  => new MessageFieldCodec<long>(attribute),
                TypeCode.UInt64 => new MessageFieldCodec<ulong>(attribute),
                _ => throw new UnreachableException()
            };
        }
        else
        {
            codec = new CollectionMessageFieldCodec(this, attribute);
        }

        return _valueCodecs.GetOrAdd((type, attribute), codec);
    }

    private Enum GetCommand([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        if (_messageCommands.TryGetValue(type, out var command))
            return command;

        var property = type.GetProperty("Command",
            BindingFlags.Public | BindingFlags.Static);

        if (property == null)
            throw new InvalidOperationException($"Type '{type.Name}' does not have a static Command property");

        return _messageCommands.GetOrAdd(type, (Enum)property.GetValue(null)!);
    }

    private Type GetEnumerableElementType(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type enumerableType)
    {
        var ex = new ArgumentException($"Cannot determine element type for {enumerableType.Name}");

        if (enumerableType.IsArray)
            return enumerableType.GetElementType() ?? throw ex;

        var enumerableInterface = enumerableType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                                                      ||  i.GetGenericTypeDefinition() == typeof(IDictionary<,>)));

        if (enumerableInterface != null)
            return enumerableInterface.GetGenericArguments().Last() ?? throw ex;

        if (enumerableType.IsGenericType &&
            typeof(IEnumerable).IsAssignableFrom(enumerableType.GetGenericTypeDefinition()))
        {
            return enumerableType.GetGenericArguments()[0] ?? throw ex;
        }

        throw ex;
    }

    private MessageMember[] GetSerializableMembers(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        if (_members.TryGetValue(type, out var members))
            return members;

        return _members.GetOrAdd(type, FindSerializableMembers(type).OrderBy(m => m.Attribute.Order).ToArray());
    }

    private static List<MessageMember> FindSerializableMembers(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        var members = new List<MessageMember>();
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        foreach (var field in fields)
        {
            var attribute = field.GetCustomAttribute<MessageFieldAttribute>();
            if (attribute != null)
                members.Add(new MessageMember(field, attribute));
        }

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => p.CanRead || p.CanWrite);

        foreach (var property in properties)
        {
            var attribute = property.GetCustomAttribute<MessageFieldAttribute>();
            if (attribute != null)
                members.Add(new MessageMember(property, attribute));
        }

        return members;
    }

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    private Type? ToSupportedFieldType(Type? type)
    {
        return Type.GetTypeCode(type) switch
        {
            TypeCode.Object   => type != null && _typeSet.TryGetValue(type, out var v) ? v : null,
            TypeCode.Boolean  => typeof(bool),
            TypeCode.Char     => typeof(char),
            TypeCode.SByte    => typeof(sbyte),
            TypeCode.Byte     => typeof(byte),
            TypeCode.Int16    => typeof(short),
            TypeCode.UInt16   => typeof(ushort),
            TypeCode.Int32    => typeof(int),
            TypeCode.UInt32   => typeof(uint),
            TypeCode.Int64    => typeof(long),
            TypeCode.UInt64   => typeof(ulong),
            TypeCode.Single   => typeof(float),
            TypeCode.Double   => typeof(double),
            TypeCode.Decimal  => typeof(decimal),
            TypeCode.DateTime => typeof(DateTime),
            TypeCode.String   => typeof(string),
            _ => null
        };
    }

    void IMessageFieldCodec.Encode(BinaryWriter writer, object value,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type sourceType)
    {
        EncodeValue(writer, value, sourceType, _defaultAttribute);
    }

    object IMessageFieldCodec.Decode(BinaryReader reader,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type targetType)
    {
        return DecodeValue(reader, targetType, _defaultAttribute);
    }

    // Attribute.Equals compares every field through reflection, so attributes are keyed by reference
    private sealed class ValueCodecKeyComparer : IEqualityComparer<(Type Type, IMessageFieldAttribute Attribute)>
    {
        public static readonly ValueCodecKeyComparer Instance = new();

        public bool Equals((Type Type, IMessageFieldAttribute Attribute) x, (Type Type, IMessageFieldAttribute Attribute) y) =>
            x.Type == y.Type && ReferenceEquals(x.Attribute, y.Attribute);

        public int GetHashCode((Type Type, IMessageFieldAttribute Attribute) key) =>
            HashCode.Combine(key.Type, RuntimeHelpers.GetHashCode(key.Attribute));
    }
}
