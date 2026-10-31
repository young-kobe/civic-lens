namespace CivicLens.Publication.Contracts;

/// <summary>
/// The latest action that Congress.gov listed for the bill of one action record, and when Civic Lens read it.
/// The record file never holds it, because a bill moves after approval and a record revision stays frozen.
/// </summary>
public sealed record PublishedBillStatus
{
    public const int MaximumTextLength = 1_000;

    public required string RecordId { get; init; }
    public required string Text { get; init; }
    public required DateOnly Date { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }

    public void Validate()
    {
        if (!PublicationProtocol.IsRecordId(PublishedRecordKind.Action, RecordId) || string.IsNullOrWhiteSpace(Text) ||
            Text.Length > MaximumTextLength || Text != Text.Trim() || ObservedAtUtc.Offset != TimeSpan.Zero)
            throw new InvalidDataException("Published bill status is invalid.");
    }
}
