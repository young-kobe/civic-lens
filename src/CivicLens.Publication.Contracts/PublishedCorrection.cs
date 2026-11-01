using System.Text.Json.Serialization;

namespace CivicLens.Publication.Contracts;

/// <summary>A null note means a code update. A note means a person correction. FactsUnchanged marks a code update that rewrote the model text while the source facts stayed the same.</summary>
public sealed record PublishedCorrection
{
    public const int MaximumNoteLength = 500;
    public const int MaximumNotedEntries = 20;

    public required int ReplacedRevisionNumber { get; init; }
    public required DateTimeOffset PublishedAtUtc { get; init; }
    public string? Note { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool FactsUnchanged { get; init; }

    public static int NotedCount(IEnumerable<PublishedCorrection> corrections) => corrections.Count(item => item.Note is not null);

    public static bool IsAtNoteLimit(IEnumerable<PublishedCorrection> corrections) => NotedCount(corrections) >= MaximumNotedEntries;

    /// <summary>A code update replaces a newest code update of the same kind, so a run of one kind stays one entry.</summary>
    public static PublishedCorrection[] Prepend(PublishedCorrection[] kept, PublishedCorrection entry) =>
        entry.Note is null && kept is [{ Note: null } newest, ..] && newest.FactsUnchanged == entry.FactsUnchanged ? [entry, .. kept[1..]] : [entry, .. kept];

    public static void ValidateAll(PublishedCorrection[]? corrections, int revisionNumber)
    {
        if (corrections is null || corrections.Any(item => item is null) || NotedCount(corrections) > MaximumNotedEntries)
            throw new InvalidDataException($"A published record lists at most {MaximumNotedEntries} corrections with a note.");
        var newerRevision = revisionNumber;
        PublishedCorrection? newer = null;
        foreach (var correction in corrections)
        {
            if (correction.ReplacedRevisionNumber < 1 || correction.ReplacedRevisionNumber >= newerRevision)
                throw new InvalidDataException("Corrections list replaced revisions from the newest to the oldest.");
            if (correction.Note is not null && !IsNote(correction.Note))
                throw new InvalidDataException($"A correction note has 1 to {MaximumNoteLength} characters and no NUL.");
            if (correction.Note is not null && correction.FactsUnchanged)
                throw new InvalidDataException("Only a code update can say that the facts did not change.");
            if (correction.Note is null && newer is { Note: null } && newer.FactsUnchanged == correction.FactsUnchanged)
                throw new InvalidDataException("Two code updates of one kind never sit next to each other in the corrections.");
            newerRevision = correction.ReplacedRevisionNumber;
            newer = correction;
        }
    }

    public static bool IsNote(string note) =>
        !string.IsNullOrWhiteSpace(note) && note.Length <= MaximumNoteLength && !note.Contains('\0');
}
