namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class PublishedBillHistoryActionRow
{
    public string DetailJobId { get; set; } = string.Empty;
    public int ProjectionVersion { get; set; }
    public int Position { get; set; }
    public DateOnly ActionDate { get; set; }
    public TimeOnly? ActionTime { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? ActionCode { get; set; }
    public int? SourceSystemCode { get; set; }
    public string? SourceSystemName { get; set; }
}
