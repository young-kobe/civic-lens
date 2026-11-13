namespace CivicLens.PublicWeb.Publication;

/// <summary>
/// The bill that a vote is about, as the bill card shows it. The line is the labeled one-line summary and may be absent.
/// <c>StatusLabel</c> comes from <c>BillStatusMeaning.Label</c> and is null when no status rule matches the latest action. <c>PublicLaw</c> is set only when that action makes the bill law.
/// <c>StatusDate</c> and <c>StatusReadAtUtc</c> are the date of the latest action and the time that Civic Lens first read it.
/// </summary>
public sealed record VoteBillData(string RecordId, string Title, string? Line)
{
    public string? StatusLabel { get; init; }
    public string? PublicLaw { get; init; }
    public DateOnly? StatusDate { get; init; }
    public DateTimeOffset? StatusReadAtUtc { get; init; }
}
