using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CivicLens.Application.Relationships;

public static class RelationshipFingerprint
{
    public static string Compute(RelationshipPair pair)
    {
        var span = pair.Match.Span;
        var text = string.Join('|', "relationship-fingerprint-v1", pair.PairId,
            pair.Statement.Revision.RevisionNumber.ToString(CultureInfo.InvariantCulture),
            pair.Target.RevisionNumber.ToString(CultureInfo.InvariantCulture), pair.Match.Basis, span.Start.ToString(CultureInfo.InvariantCulture),
            span.Length.ToString(CultureInfo.InvariantCulture), RelationshipDraftTask.TaskVersion, RelationshipDraftTask.PromptVersion);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
