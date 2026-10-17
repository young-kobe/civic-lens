using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CivicLens.Core.Statements;

public static class GateLabelSplit
{
    public const int HeldOutPercent = 30;

    public static bool IsHeldOut(string candidateId)
    {
        ArgumentNullException.ThrowIfNull(candidateId);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(candidateId));
        return BinaryPrimitives.ReadUInt32BigEndian(hash) % 100 < HeldOutPercent;
    }
}
