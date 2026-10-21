namespace CivicLens.Publication.Contracts;

/// <summary>A record that a person took out of the release. The notice page keeps its ID, dates, note, and correction history only.</summary>
public sealed record PublishedWithdrawal
{
    public required PublishedRecordKind Kind { get; init; }
    public required string RecordId { get; init; }
    public required int WithdrawnRevisionNumber { get; init; }
    public required DateTimeOffset FirstPublishedAtUtc { get; init; }
    public required DateTimeOffset WithdrawnAtUtc { get; init; }
    public required string Note { get; init; }
    public required PublishedCorrection[] Corrections { get; init; }

    public void Validate()
    {
        if (!PublicationProtocol.IsRecordId(Kind, RecordId) || WithdrawnRevisionNumber < 1 || WithdrawnAtUtc < FirstPublishedAtUtc)
            throw new InvalidDataException("Published withdrawal identity or dates are invalid.");
        if (Note is null || !PublishedCorrection.IsNote(Note))
            throw new InvalidDataException($"A withdrawal note has 1 to {PublishedCorrection.MaximumNoteLength} characters and no NUL.");
        PublishedCorrection.ValidateAll(Corrections, WithdrawnRevisionNumber);
    }
}
