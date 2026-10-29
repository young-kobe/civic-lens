using System.Security.Cryptography;
using System.Text;

namespace CivicLens.Application.ExecutiveActions;

public static class ExecutiveActionIdentity
{
    public static string RecordId(string sourceId, string recordDocumentNumber) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"executive-action:{sourceId.Length}:{sourceId}{recordDocumentNumber}")));
}
