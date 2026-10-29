namespace CivicLens.Infrastructure.ExecutiveActions.Persistence;

/// <summary>Binds one Federal Register document to the revision that it made. A known document makes no second revision.</summary>
internal sealed class ExecutiveActionDocumentRow
{
    public string SourceId { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public DateOnly PublishedOn { get; set; }
    public string DocumentJobId { get; set; } = string.Empty;
}
