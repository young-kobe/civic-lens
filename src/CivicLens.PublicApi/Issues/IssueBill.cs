namespace CivicLens.PublicApi.Issues;

/// <summary>One bill on an issue. <c>EventDate</c> is the day of its latest event, not the day it began.</summary>
public sealed record IssueBill(string RecordId, string Headline, string? Label, DateOnly EventDate, string? BillLine);
