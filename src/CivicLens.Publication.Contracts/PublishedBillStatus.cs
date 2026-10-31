namespace CivicLens.Publication.Contracts;

/// <summary>
/// The latest action that Congress.gov listed for the bill of one action record, and when Civic Lens read it.
/// The record file never holds it, because a bill moves after approval and a record revision stays frozen.
/// </summary>
public sealed record PublishedBillStatus
{
    // A row holds at most 3 bytes for each of 1,000 characters, because a control character is refused and the encoder keeps other characters as UTF-8.
    public const int MaximumTextLength = 1_000;

    public required string RecordId { get; init; }
    public required string Text { get; init; }
    public required DateOnly Date { get; init; }
    /// <summary>The time of the first read of this text and date. A later read of the same action keeps it; see ReleaseBillStatuses.</summary>
    public required DateTimeOffset ObservedAtUtc { get; init; }

    public void Validate()
    {
        if (!PublicationProtocol.IsRecordId(PublishedRecordKind.Action, RecordId) || string.IsNullOrWhiteSpace(Text) ||
            Text.Length > MaximumTextLength || Text != Text.Trim() || Text.Any(char.IsControl) || ObservedAtUtc.Offset != TimeSpan.Zero)
            throw new InvalidDataException("Published bill status is invalid.");
    }
}
