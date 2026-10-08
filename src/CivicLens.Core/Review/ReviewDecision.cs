using System.Collections.Immutable;

namespace CivicLens.Core.Review;

public enum ReviewDecisionKind { Approve, RequestChanges, WithdrawApproval }

public sealed record ReviewDecision(string DecisionId, string DraftId, int RevisionNumber,
    int ReviewStateVersion, ReviewDecisionKind Kind, string ActorSubject, string? Note,
    ImmutableArray<string> ResolvedDecisionIds, DateTimeOffset CreatedAtUtc);
