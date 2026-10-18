using CivicLens.Core.Analysis;

namespace CivicLens.Application.Relationships;

public sealed record RelationshipDraftRun(AnalysisRun Run, string? Reason);
