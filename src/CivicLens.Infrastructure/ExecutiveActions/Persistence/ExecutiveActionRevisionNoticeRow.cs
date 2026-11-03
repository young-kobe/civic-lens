namespace CivicLens.Infrastructure.ExecutiveActions.Persistence;

internal sealed class ExecutiveActionRevisionNoticeRow
{
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public short Position { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
}
