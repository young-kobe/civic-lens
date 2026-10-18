using CivicLens.Core.Relationships;

namespace CivicLens.Infrastructure.Relationships;

internal readonly record struct RelationshipPairScope(string? StatementId, RelationshipRecordKind? TargetKind, string? TargetId)
{
    public static RelationshipPairScope All => default;

    public static RelationshipPairScope OfStatement(string statementId) => new(statementId, null, null);

    public static RelationshipPairScope OfTarget(RelationshipRecordKind kind, string recordId) => new(null, kind, recordId);
}
