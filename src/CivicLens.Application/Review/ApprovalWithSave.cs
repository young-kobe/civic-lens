using System.Collections.Immutable;

namespace CivicLens.Application.Review;

public sealed record ApprovalWithSave(int ExpectedReviewStateVersion, string? Note, ImmutableArray<string> ResolvedDecisionIds,
    string? CorrectionNote);
