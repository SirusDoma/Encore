using System.Text;

namespace Encore;

public static class StreamExtensions
{
    public static object ReadInteger(this BinaryReader reader, TypeCode code)
    {
        return code switch
        {
            TypeCode.SByte   => reader.ReadSByte(),
            TypeCode.Byte    => reader.ReadByte(),
            TypeCode.Int16   => reader.ReadInt16(),
            TypeCode.UInt16  => reader.ReadUInt16(),
            TypeCode.Int32   => reader.ReadInt32(),
            TypeCode.UInt32  => reader.ReadUInt32(),
            TypeCode.Int64   => reader.ReadInt64(),
            TypeCode.UInt64  => reader.ReadUInt64(),
            var t => throw new NotSupportedException($"Read integer does not support '{t}'")
        };
    }

    public static string ReadString(this BinaryReader reader, Encoding encoding, TypeCode prefix = TypeCode.Empty,
        bool nullTerminated = true, int maxCount = ushort.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(encoding);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCount);

        if (reader.BaseStream.Length == reader.BaseStream.Position && prefix == TypeCode.Empty)
            return string.Empty;

        if (prefix != TypeCode.Empty)
            maxCount = Convert.ToInt32(reader.ReadInteger(prefix));

        long remaining = reader.BaseStream.Length - reader.BaseStream.Position;
        if (prefix == TypeCode.Empty && !nullTerminated)
            maxCount = (int)Math.Min(maxCount, remaining);

        if (maxCount < 0)
            throw new FormatException("String length cannot be negative");

        if (prefix != TypeCode.Empty || !nullTerminated)
        {
            if (maxCount > remaining)
                throw new FormatException($"String prefix length ({maxCount} bytes) exceeds the remaining data ({remaining} bytes)");

            return encoding.GetString(reader.ReadBytes(maxCount));
        }

        byte[] terminator = encoding.GetBytes("\0");
        byte[] unit = new byte[terminator.Length];
        var bytes = new List<byte>();

        while (bytes.Count < maxCount)
        {
            int size = Math.Min(unit.Length, maxCount - bytes.Count);
            for (int i = 0; i < size; i++)
                unit[i] = reader.ReadByte();

            if (unit.AsSpan(0, size).SequenceEqual(terminator))
                break;

            for (int i = 0; i < size; i++)
                bytes.Add(unit[i]);
        }

        return encoding.GetString(bytes.ToArray());
    }

    public static void WriteInteger(this BinaryWriter writer, object value, TypeCode code)
    {
        object numeric = Convert.ChangeType(value, code);
        switch (code)
        {
            case TypeCode.SByte:  writer.Write((sbyte)numeric);  break;
            case TypeCode.Byte:   writer.Write((byte)numeric);   break;
            case TypeCode.Int16:  writer.Write((short)numeric);  break;
            case TypeCode.UInt16: writer.Write((ushort)numeric); break;
            case TypeCode.Int32:  writer.Write((int)numeric);    break;
            case TypeCode.UInt32: writer.Write((uint)numeric);   break;
            case TypeCode.Int64:  writer.Write((long)numeric);   break;
            case TypeCode.UInt64: writer.Write((ulong)numeric);  break;
            default: throw new NotSupportedException($"Write integer does support '{value.GetType()}'");
        }
    }

    public static int Write(this BinaryWriter writer, string value, Encoding encoding, TypeCode prefix = TypeCode.Empty,
        bool terminateWithNull = true, int maxCount = short.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(encoding);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCount);

        byte[] terminator = terminateWithNull && maxCount > 0 ? encoding.GetBytes("\0") : [];
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, terminator.Length);

        if (terminateWithNull && value.EndsWith('\0'))
            terminator = [];

        int capacity = maxCount - terminator.Length;
        byte[] bytes = encoding.GetBytes(value);
        if (bytes.Length > capacity)
            bytes = bytes.Take(capacity).ToArray();

        int count = bytes.Length + terminator.Length;
        if (prefix != TypeCode.Empty)
            writer.WriteInteger(count, prefix);

        writer.Write(bytes);
        writer.Write(terminator);

        return count;
    }
}
