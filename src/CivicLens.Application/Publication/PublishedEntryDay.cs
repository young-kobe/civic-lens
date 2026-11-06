namespace CivicLens.Application.Publication;

/// <summary>The key of an entry row, its event date, and the first publication time of its record. These are the inputs of <see cref="ReleaseRecordOrder.CongressOf(DateOnly?, DateTimeOffset)"/>.</summary>
public sealed record PublishedEntryDay(string Kind, string RecordId, int RevisionNumber, DateOnly? EventDate, DateTimeOffset FirstPublishedAtUtc)
{
    public int? Congress => ReleaseRecordOrder.CongressOf(EventDate, FirstPublishedAtUtc);
}
