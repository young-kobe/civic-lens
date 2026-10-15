namespace CivicLens.Application.Actions;

public sealed record SaveActionDraftRequest(string RecordId, int ExpectedRevisionNumber, string Headline, string Summary,
    bool KeepSuggestion, string IdempotencyKey);
