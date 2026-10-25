using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CivicLens.Application.Briefs;

public static class BriefFingerprint
{
    public const string Version = "brief-fingerprint-v1";

    public static string Compute(int cutoffReleaseNumber, ImmutableArray<BriefSelectedRecord> sent)
    {
        var lines = sent.Select(item => $"{item.Record.Kind}|{item.Record.RecordId}|{item.Record.RevisionNumber.ToString(CultureInfo.InvariantCulture)}")
            .Order(StringComparer.Ordinal);
        var text = string.Join('\n', [Version, cutoffReleaseNumber.ToString(CultureInfo.InvariantCulture), .. lines]);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
