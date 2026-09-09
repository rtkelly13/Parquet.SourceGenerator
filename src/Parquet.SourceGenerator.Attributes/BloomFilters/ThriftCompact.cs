using System;
using System.Collections.Generic;
using System.Text;

namespace Parquet.SourceGenerator.BloomFilters;

/// <summary>
/// Thrift compact protocol type identifiers, as used by the Parquet metadata encoding.
/// </summary>
internal static class ThriftType
{
    public const byte Stop = 0x00;
    public const byte BooleanTrue = 0x01;
    public const byte BooleanFalse = 0x02;
    public const byte Byte = 0x03;
    public const byte I16 = 0x04;
    public const byte I32 = 0x05;
    public const byte I64 = 0x06;
    public const byte Double = 0x07;
    public const byte Binary = 0x08;
    public const byte List = 0x09;
    public const byte Set = 0x0A;
    public const byte Map = 0x0B;
    public const byte Struct = 0x0C;
    public const byte Uuid = 0x0D;
}

/// <summary>
/// Minimal forward-only reader for the Thrift compact protocol.
/// </summary>
/// <remarks>
/// Parquet.Net has a complete implementation but keeps it <c>internal</c>, and it exposes no way to
/// re-serialize a footer. Bloom filter support needs exactly two things the public surface cannot
/// give: the ability to read <c>ColumnMetaData.bloom_filter_offset</c> without a full metadata
/// object graph, and the ability to write it back into an already-written file. Both are narrow
/// enough that a ~200 line codec is cheaper than taking a dependency or forking the library.
/// </remarks>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal ref struct ThriftCompactReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;
    private short _lastFieldId;

    public ThriftCompactReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = 0;
        _lastFieldId = 0;
    }

    public int Position => _position;

    public short LastFieldId
    {
        get => _lastFieldId;
        set => _lastFieldId = value;
    }

    /// <summary>
    /// Reads the next field header of the current struct. Returns <c>false</c> at the struct stop
    /// marker, at which point the stop byte has been consumed.
    /// </summary>
    public bool ReadFieldHeader(out short fieldId, out byte fieldType)
    {
        byte header = _data[_position++];
        if (header == ThriftType.Stop)
        {
            fieldId = 0;
            fieldType = ThriftType.Stop;
            return false;
        }

        fieldType = (byte)(header & 0x0F);
        int delta = (header & 0xF0) >> 4;
        if (delta == 0)
        {
            fieldId = (short)ZigZagDecode(ReadVarint());
        }
        else
        {
            fieldId = (short)(_lastFieldId + delta);
        }

        _lastFieldId = fieldId;
        return true;
    }

    public void ReadListHeader(out int size, out byte elementType)
    {
        byte header = _data[_position++];
        elementType = (byte)(header & 0x0F);
        size = (header & 0xF0) >> 4;
        if (size == 15)
        {
            size = (int)ReadVarint();
        }
    }

    public ulong ReadVarint()
    {
        ulong result = 0;
        int shift = 0;
        while (true)
        {
            byte b = _data[_position++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return result;
            }

            shift += 7;
        }
    }

    public long ReadI64() => ZigZagDecode(ReadVarint());

    public int ReadI32() => (int)ZigZagDecode(ReadVarint());

    public string ReadString()
    {
        int length = (int)ReadVarint();
        string value = Encoding.UTF8.GetString(_data.Slice(_position, length).ToArray(), 0, length);
        _position += length;
        return value;
    }

    /// <summary>Advances past a value of the supplied type without materializing it.</summary>
    public void SkipValue(byte type)
    {
        switch (type)
        {
            case ThriftType.BooleanTrue:
            case ThriftType.BooleanFalse:
                return;
            case ThriftType.Byte:
                _position++;
                return;
            case ThriftType.I16:
            case ThriftType.I32:
            case ThriftType.I64:
                ReadVarint();
                return;
            case ThriftType.Double:
                _position += 8;
                return;
            case ThriftType.Uuid:
                _position += 16;
                return;
            case ThriftType.Binary:
            {
                // Read the length into a local first: `_position += (int)ReadVarint()` captures the
                // pre-call cursor on the left of the compound assignment and then overwrites the
                // advance ReadVarint made, silently desynchronising the whole parse.
                int byteLength = (int)ReadVarint();
                _position += byteLength;
                return;
            }
            case ThriftType.List:
            case ThriftType.Set:
            {
                ReadListHeader(out int size, out byte elementType);
                for (int i = 0; i < size; i++)
                {
                    SkipValue(elementType);
                }

                return;
            }
            case ThriftType.Map:
            {
                int count = (int)ReadVarint();
                if (count == 0)
                {
                    return;
                }

                byte kv = _data[_position++];
                byte keyType = (byte)((kv & 0xF0) >> 4);
                byte valueType = (byte)(kv & 0x0F);
                for (int i = 0; i < count; i++)
                {
                    SkipValue(keyType);
                    SkipValue(valueType);
                }

                return;
            }
            case ThriftType.Struct:
            {
                short saved = _lastFieldId;
                _lastFieldId = 0;
                while (ReadFieldHeader(out _, out byte innerType))
                {
                    SkipValue(innerType);
                }

                _lastFieldId = saved;
                return;
            }
            default:
                throw new FormatException("Unsupported Thrift compact type id " + type + ".");
        }
    }

    public ReadOnlySpan<byte> Slice(int start, int length) => _data.Slice(start, length);

    private static long ZigZagDecode(ulong value) => (long)(value >> 1) ^ -(long)(value & 1);
}

/// <summary>
/// Minimal growable writer for the Thrift compact protocol, sufficient to re-emit a Parquet footer
/// with additional <c>ColumnMetaData</c> fields spliced in.
/// </summary>
internal sealed class ThriftCompactWriter
{
    private byte[] _buffer;
    private int _length;
    private short _lastFieldId;

    public ThriftCompactWriter(int capacity)
    {
        _buffer = new byte[Math.Max(64, capacity)];
    }

    public int Length => _length;

    public short LastFieldId
    {
        get => _lastFieldId;
        set => _lastFieldId = value;
    }

    public void WriteFieldHeader(short fieldId, byte fieldType)
    {
        int delta = fieldId - _lastFieldId;
        if (delta > 0 && delta <= 15)
        {
            WriteByte((byte)((delta << 4) | fieldType));
        }
        else
        {
            WriteByte(fieldType);
            WriteVarint(ZigZagEncode(fieldId));
        }

        _lastFieldId = fieldId;
    }

    public void WriteStop() => WriteByte(ThriftType.Stop);

    public void WriteListHeader(int size, byte elementType)
    {
        if (size < 15)
        {
            WriteByte((byte)((size << 4) | elementType));
        }
        else
        {
            WriteByte((byte)(0xF0 | elementType));
            WriteVarint((ulong)size);
        }
    }

    public void WriteI32(int value) => WriteVarint(ZigZagEncode(value));

    public void WriteI64(long value) => WriteVarint(ZigZagEncode(value));

    public void WriteByte(byte value)
    {
        EnsureCapacity(1);
        _buffer[_length++] = value;
    }

    public void WriteRaw(ReadOnlySpan<byte> bytes)
    {
        EnsureCapacity(bytes.Length);
        bytes.CopyTo(new Span<byte>(_buffer, _length, bytes.Length));
        _length += bytes.Length;
    }

    public void WriteVarint(ulong value)
    {
        while (true)
        {
            if ((value & ~0x7FUL) == 0)
            {
                WriteByte((byte)value);
                return;
            }

            WriteByte((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
    }

    public byte[] ToArray()
    {
        var result = new byte[_length];
        Array.Copy(_buffer, result, _length);
        return result;
    }

    public void CopyTo(List<byte> destination)
    {
        for (int i = 0; i < _length; i++)
        {
            destination.Add(_buffer[i]);
        }
    }

    private static ulong ZigZagEncode(long value) => (ulong)((value << 1) ^ (value >> 63));

    private void EnsureCapacity(int extra)
    {
        if (_length + extra <= _buffer.Length)
        {
            return;
        }

        int capacity = _buffer.Length;
        while (capacity < _length + extra)
        {
            capacity *= 2;
        }

        Array.Resize(ref _buffer, capacity);
    }
}
