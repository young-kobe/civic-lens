namespace CivicLens.PublicApi.Issues;

/// <summary>One bill on an issue. <c>EventDate</c> is the day of its latest event, not the day it began.
/// <c>Sponsor</c> is the name of the first sponsor in stored order, or null when the release names no sponsor.</summary>
public sealed record IssueBill(string RecordId, string Headline, string? Label, DateOnly EventDate, string? BillLine, string? Sponsor);
