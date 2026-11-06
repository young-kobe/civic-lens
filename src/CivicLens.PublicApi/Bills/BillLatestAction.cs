namespace CivicLens.PublicApi.Bills;

/// <summary>The latest action that Congress.gov lists for a bill, as Civic Lens last read it. <c>ReadAtUtc</c> is the time of the first read of this text and date.</summary>
public sealed record BillLatestAction(string Text, DateOnly Date, DateTimeOffset ReadAtUtc);
