using CivicLens.Application.Review;

namespace CivicLens.Application.Votes;

public sealed record SaveVoteDraftRequest(string RecordId, int ExpectedRevisionNumber, string Headline, string Summary,
    SuggestionChoice Suggestion, string IdempotencyKey, ApprovalWithSave? Approval = null);
