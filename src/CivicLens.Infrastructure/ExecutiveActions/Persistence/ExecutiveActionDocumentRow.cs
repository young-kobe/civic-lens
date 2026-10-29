namespace CivicLens.Infrastructure.ExecutiveActions.Persistence;

/// <summary>Binds one Federal Register document to the first revision that holds it. A known document makes no second revision.
/// A correction notice whose original is not recorded yet has no revision and waits here with its notice.</summary>
internal sealed class ExecutiveActionDocumentRow
{
    public string SourceId { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
    public int? RevisionNumber { get; set; }
    public string? NoticeJson { get; set; }
    public DateOnly PublishedOn { get; set; }
    public string DocumentJobId { get; set; } = string.Empty;
}
