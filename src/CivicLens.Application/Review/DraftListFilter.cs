namespace CivicLens.Application.Review;

public readonly record struct DraftListFilter(DraftStatusFilter Status, string? IssueId = null, string? OfficialId = null,
    DateOnly? From = null, DateOnly? To = null);
