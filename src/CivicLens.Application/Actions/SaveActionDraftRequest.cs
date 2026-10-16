using CivicLens.Application.Review;

namespace CivicLens.Application.Actions;

public sealed record SaveActionDraftRequest(string RecordId, int ExpectedRevisionNumber, string Headline, string Summary,
    SuggestionChoice Suggestion, string IdempotencyKey);
