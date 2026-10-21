namespace CivicLens.Publication.Contracts;

/// <summary>A null note means a code update from the source. A note means a person correction.</summary>
public sealed record PublishedCorrection
{
    public const int MaximumNoteLength = 500;
    public const int MaximumEntries = 20;

    public required int ReplacedRevisionNumber { get; init; }
    public required DateTimeOffset PublishedAtUtc { get; init; }
    public string? Note { get; init; }

    public static void ValidateAll(PublishedCorrection[]? corrections, int revisionNumber)
    {
        if (corrections is null || corrections.Length > MaximumEntries || corrections.Any(item => item is null))
            throw new InvalidDataException($"A published record lists at most {MaximumEntries} corrections.");
        var newerRevision = revisionNumber;
        foreach (var correction in corrections)
        {
            if (correction.ReplacedRevisionNumber < 1 || correction.ReplacedRevisionNumber >= newerRevision)
                throw new InvalidDataException("Corrections list replaced revisions from the newest to the oldest.");
            if (correction.Note is not null && !IsNote(correction.Note))
                throw new InvalidDataException($"A correction note has 1 to {MaximumNoteLength} characters and no NUL.");
            newerRevision = correction.ReplacedRevisionNumber;
        }
    }

    public static bool IsNote(string note) =>
        !string.IsNullOrWhiteSpace(note) && note.Length <= MaximumNoteLength && !note.Contains('\0');
}
