using System.Text.Json.Serialization;

namespace CivicLens.Publication.Contracts;

/// <summary>A null note means a code update. A note means a person correction. FactsUnchanged marks a code update that changed only the headline or the summary of a vote.</summary>
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

    /// <summary>A code update replaces the code update of its kind in the newest run of code updates, so a run holds at most one of each kind.</summary>
    public static PublishedCorrection[] Prepend(PublishedCorrection[] kept, PublishedCorrection entry)
    {
        if (entry.Note is not null) return [entry, .. kept];
        var run = kept.TakeWhile(item => item.Note is null).Count();
        return [entry, .. kept[..run].Where(item => item.FactsUnchanged != entry.FactsUnchanged), .. kept[run..]];
    }

    public static void ValidateAll(PublishedCorrection[]? corrections, int revisionNumber)
    {
        if (corrections is null || corrections.Any(item => item is null) || NotedCount(corrections) > MaximumNotedEntries)
            throw new InvalidDataException($"A published record lists at most {MaximumNotedEntries} corrections with a note.");
        var newerRevision = revisionNumber;
        var kindsInRun = new HashSet<bool>();
        foreach (var correction in corrections)
        {
            if (correction.ReplacedRevisionNumber < 1 || correction.ReplacedRevisionNumber >= newerRevision)
                throw new InvalidDataException("Corrections list replaced revisions from the newest to the oldest.");
            if (correction.Note is not null && !IsNote(correction.Note))
                throw new InvalidDataException($"A correction note has 1 to {MaximumNoteLength} characters and no NUL.");
            if (correction.Note is not null && correction.FactsUnchanged)
                throw new InvalidDataException("Only a code update can say that the facts did not change.");
            if (correction.Note is not null) kindsInRun.Clear();
            else if (!kindsInRun.Add(correction.FactsUnchanged))
                throw new InvalidDataException("Between two person corrections the list holds at most one code update of each kind.");
            newerRevision = correction.ReplacedRevisionNumber;
        }
    }

    public static bool IsNote(string note) =>
        !string.IsNullOrWhiteSpace(note) && note.Length <= MaximumNoteLength && !note.Contains('\0');
}
