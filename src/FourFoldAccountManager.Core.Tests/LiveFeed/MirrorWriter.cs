using System.Buffers.Binary;
using System.Text;
using FourFoldAccountManager.Core.LiveFeed;

namespace FourFoldAccountManager.Core.Tests.LiveFeed;

// Builds Mirror frames the way the game's server writes them, so tests never need captured traffic.
internal sealed class MirrorWriter
{
    private readonly List<byte> _bytes = [];

    public byte[] ToArray() => [.. _bytes];

    public MirrorWriter Byte(byte value)
    {
        _bytes.Add(value);
        return this;
    }

    public MirrorWriter Bytes(ReadOnlySpan<byte> value)
    {
        foreach (var b in value)
        {
            _bytes.Add(b);
        }

        return this;
    }

    public MirrorWriter UShort(ushort value) => Byte((byte)value).Byte((byte)(value >> 8));

    public MirrorWriter Bool(bool value) => Byte(value ? (byte)1 : (byte)0);

    public MirrorWriter Double(double value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(buffer, value);
        return Bytes(buffer);
    }

    public MirrorWriter Float(float value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
        return Bytes(buffer);
    }

    // Mirror's Compression.CompressVarUInt.
    public MirrorWriter VarUInt(ulong value)
    {
        if (value <= 240)
        {
            return Byte((byte)value);
        }

        if (value <= 2287)
        {
            return Byte((byte)(((value - 240) >> 8) + 241)).Byte((byte)((value - 240) & 0xFF));
        }

        if (value <= 67823)
        {
            return Byte(249).Byte((byte)((value - 2288) >> 8)).Byte((byte)((value - 2288) & 0xFF));
        }

        var count = value <= 0xFFFFFF ? 3 : value <= 0xFFFFFFFF ? 4 : 8;
        Byte((byte)(247 + count));
        for (var i = 0; i < count; i++)
        {
            Byte((byte)(value >> (8 * i)));
        }

        return this;
    }

    // Mirror's zigzag varint (WriteVarInt / WriteVarLong).
    public MirrorWriter VarInt(long value) => VarUInt((ulong)((value << 1) ^ (value >> 63)));

    public MirrorWriter String(string? value)
    {
        if (value is null)
        {
            return UShort(0);
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        return UShort((ushort)(bytes.Length + 1)).Bytes(bytes);
    }

    // One WebSocket frame: the batch timestamp, then each message behind its varuint length.
    public static byte[] Batch(params byte[][] messages)
    {
        var writer = new MirrorWriter().Double(1234.5);
        foreach (var message in messages)
        {
            writer.VarUInt((ulong)message.Length).Bytes(message);
        }

        return writer.ToArray();
    }

    // A TargetRpc as Mirror sends it: RpcMessage id, netId, component index, function hash, payload with size + 1.
    public static byte[] Rpc(string signature, byte[] payload) =>
        new MirrorWriter()
            .UShort(MirrorHash.MessageId("Mirror.RpcMessage"))
            .VarUInt(42)
            .Byte(0)
            .UShort(MirrorHash.FunctionHash(signature))
            .VarUInt((ulong)payload.Length + 1)
            .Bytes(payload)
            .ToArray();

    // A NetworkMessage struct: its id, then its fields.
    public static byte[] Message(string typeFullName, byte[] payload) =>
        new MirrorWriter().UShort(MirrorHash.MessageId(typeFullName)).Bytes(payload).ToArray();
}
