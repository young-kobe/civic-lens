namespace CivicLens.PublicWeb.Publication;

/// <summary>The bill that a vote is about, as the bill card shows it. The line is the labeled one-line summary and may be absent.</summary>
public sealed record VoteBillData(string RecordId, string Title, string? Line);
