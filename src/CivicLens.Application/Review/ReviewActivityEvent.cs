using CivicLens.Core.Review;

namespace CivicLens.Application.Review;

public enum ReviewActivityKind { DraftCreated, RevisionSaved, DecisionRecorded }

/// <summary>DecisionKind is set only for DecisionRecorded.</summary>
public sealed record ReviewActivityEvent(ReviewActivityKind Kind, DateTimeOffset OccurredAt, string ActorSubject,
    string DraftId, string Headline, ReviewDecisionKind? DecisionKind);
