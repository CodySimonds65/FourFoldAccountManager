using System.Buffers.Binary;
using System.Text;

namespace FourFoldAccountManager.Core.LiveFeed;

// The game's traffic is untrusted input: a short or malformed buffer throws this, and the decoder turns it into a
// dropped message. Its message never contains frame contents.
internal sealed class MirrorFormatException : Exception
{
    public MirrorFormatException() : base("The data doesn't match Mirror's format.")
    {
    }
}

// Reads Mirror's wire primitives (NetworkReader, Compression). Every read is bounds-checked.
internal ref struct MirrorReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _position;

    public MirrorReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
    }

    public readonly int Remaining => _buffer.Length - _position;

    public byte ReadByte()
    {
        Need(1);
        return _buffer[_position++];
    }

    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        if (count < 0)
        {
            throw new MirrorFormatException();
        }

        Need(count);
        var slice = _buffer.Slice(_position, count);
        _position += count;
        return slice;
    }

    public ushort ReadUShort() => BinaryPrimitives.ReadUInt16LittleEndian(ReadBytes(2));

    public double ReadDouble() => BinaryPrimitives.ReadDoubleLittleEndian(ReadBytes(8));

    public bool ReadBool() => ReadByte() != 0;

    // Compression.DecompressVarUInt.
    public ulong ReadVarUInt()
    {
        var b0 = ReadByte();
        if (b0 < 241)
        {
            return b0;
        }

        var b1 = ReadByte();
        if (b0 <= 248)
        {
            return 240 + ((ulong)(b0 - 241) << 8) + b1;
        }

        var b2 = ReadByte();
        if (b0 == 249)
        {
            return 2288 + ((ulong)b1 << 8) + b2;
        }

        // 250..255: the value follows little-endian in (b0 - 247) bytes, two of which are already read.
        var value = b1 | ((ulong)b2 << 8);
        for (var i = 2; i < b0 - 247; i++)
        {
            value |= (ulong)ReadByte() << (8 * i);
        }

        return value;
    }

    // Compression.DecompressVarInt: zigzag over the varuint.
    public long ReadVarInt()
    {
        var value = ReadVarUInt();
        return (long)(value >> 1) ^ -(long)(value & 1);
    }

    public int ReadVarInt32()
    {
        var value = ReadVarInt();
        return value is >= int.MinValue and <= int.MaxValue ? (int)value : throw new MirrorFormatException();
    }

    // NetworkReader.ReadString: ushort (byte length + 1), 0 meaning null, then UTF-8.
    public string? ReadString(int maxBytes)
    {
        var length = StringLength(maxBytes);
        return length < 0 ? null : Encoding.UTF8.GetString(ReadBytes(length));
    }

    // For fields the feed must never keep (a username, an error): checks the length without decoding the text.
    public void SkipString(int maxBytes)
    {
        var length = StringLength(maxBytes);
        if (length > 0)
        {
            ReadBytes(length);
        }
    }

    // WriteArray: varuint (count + 1), 0 meaning null. Returns -1 for null.
    public int ReadArrayCount(int maxCount)
    {
        var value = ReadVarUInt();
        if (value == 0)
        {
            return -1;
        }

        return value - 1 <= (ulong)maxCount ? (int)(value - 1) : throw new MirrorFormatException();
    }

    // ReadArraySegmentAndSize: varuint (size + 1), 0 meaning empty, then the bytes.
    public ReadOnlySpan<byte> ReadSegmentAndSize()
    {
        var value = ReadVarUInt();
        if (value == 0)
        {
            return default;
        }

        return value - 1 <= int.MaxValue ? ReadBytes((int)(value - 1)) : throw new MirrorFormatException();
    }

    private int StringLength(int maxBytes)
    {
        var prefix = ReadUShort();
        if (prefix == 0)
        {
            return -1;
        }

        return prefix - 1 <= maxBytes ? prefix - 1 : throw new MirrorFormatException();
    }

    private readonly void Need(int count)
    {
        if (count > Remaining)
        {
            throw new MirrorFormatException();
        }
    }
}
