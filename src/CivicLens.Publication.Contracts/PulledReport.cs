namespace CivicLens.Publication.Contracts;

/// <summary>One reader report as the search service hands it to the puller. It holds nothing about the reporter.</summary>
public sealed record PulledReport
{
    public required Guid ReportId { get; init; }
    public required string RecordId { get; init; }
    public required int RevisionNumber { get; init; }
    public required PublishedRecordKind Kind { get; init; }
    public required string Category { get; init; }
    public required string Text { get; init; }
    public required DateTimeOffset ReceivedAtUtc { get; init; }

    public void Validate()
    {
        if (ReportId == Guid.Empty || !PublicationProtocol.IsAnyRecordId(RecordId) || RevisionNumber < 1 ||
            !Enum.IsDefined(Kind) || !ReaderReportProtocol.IsCategory(Category) || Text is null || !ReaderReportProtocol.IsText(Category, Text))
            throw new InvalidDataException("A pulled report is invalid.");
    }
}
