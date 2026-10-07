namespace FourFoldAccountManager.Core.LiveFeed;

// Mirror's ids, computed the way Mirror computes them, so the allowlist is written as readable names and signatures
// instead of magic numbers.
public static class MirrorHash
{
    // Mirror's Extensions.GetStableHashCode: FNV-1a over the low byte of each char.
    public static int StableHash(string text)
    {
        var hash = 2166136261u;
        foreach (var ch in text)
        {
            hash ^= (byte)ch;
            hash *= 16777619u;
        }

        return (int)hash;
    }

    // NetworkMessageId<T>.Id: GetStableHashCode16 of the message type's full name.
    public static ushort MessageId(string typeFullName)
    {
        var hash = StableHash(typeFullName);
        return (ushort)((hash >> 16) ^ hash);
    }

    // RemoteProcedureCalls.RegisterDelegate: the low 16 bits of the full signature's hash. Not the same fold as MessageId.
    public static ushort FunctionHash(string signature) => (ushort)(StableHash(signature) & 0xFFFF);
}
