namespace CivicLens.Publication.Contracts;

/// <summary>One status row for each action record of a release that has a stored status. The search service never reads it.</summary>
public sealed record PublishedBillStatuses
{
    public const int MaximumBytesPerRow = 4 * 1024;
    public const int MaximumBytes = PublicationProtocol.MaximumRecordsPerRelease * MaximumBytesPerRow;

    public required PublishedBillStatus[] Records { get; init; }

    public void Validate()
    {
        if (Records is null || Records.Length > PublicationProtocol.MaximumRecordsPerRelease || Records.Any(record => record is null))
            throw new InvalidDataException("Published bill statuses are invalid.");
        foreach (var record in Records) record.Validate();
        if (!PublishedEvidenceRules.AreDistinctAndNotBlank(Records.Select(record => record.RecordId)))
            throw new InvalidDataException("Published bill status record IDs must be distinct.");
    }
}
