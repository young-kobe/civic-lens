using CivicLens.Application.Analysis;
using CivicLens.Core.Relationships;

namespace CivicLens.Application.Relationships;

public sealed record RelationshipDraftListItem(string PairId, string StatementCandidateId, RelationshipRecordKind TargetKind, string TargetRecordId,
    AnalysisState Status, RelationshipDraftView View, string? Code, int Attempts, string? LatestRunId);
