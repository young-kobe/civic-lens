using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CivicLens.Application.Relationships;

public static class RelationshipFingerprint
{
    public const string Version = "relationship-fingerprint-v1";

    public static string Compute(RelationshipPair pair)
    {
        var span = pair.Match.Span;
        var facts = pair.Target.Facts.Select(fact => $"{fact.Id}={fact.Text}");
        var text = string.Join('|', Version, pair.PairId,
            pair.Statement.Revision.RevisionNumber.ToString(CultureInfo.InvariantCulture), pair.OfficialId, pair.OfficialRole, string.Join('\n', facts),
            pair.Match.Basis, span.Start.ToString(CultureInfo.InvariantCulture), span.Length.ToString(CultureInfo.InvariantCulture),
            RelationshipDraftTask.TaskVersion, RelationshipDraftTask.PromptVersion);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
