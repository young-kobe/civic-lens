using System.Security.Cryptography;
using System.Text;
using CivicLens.Core.Relationships;

namespace CivicLens.Application.Relationships;

public sealed record RelationshipPair(ApprovedStatement Statement, RelationshipTarget Target, RelationshipMatch Match, string OfficialId,
    string OfficialRole)
{
    public string PairId => IdOf(Statement.RecordId, Target.Kind, Target.RecordId);

    public static string IdOf(string statementRecordId, RelationshipRecordKind targetKind, string targetRecordId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"relationship-v1|{statementRecordId}|{targetKind.ToString().ToLowerInvariant()}|{targetRecordId}")));
}
