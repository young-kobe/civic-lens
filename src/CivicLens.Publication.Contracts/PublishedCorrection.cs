using System.Text.Json.Serialization;

namespace CivicLens.Publication.Contracts;

/// <summary>A null note means a code update. A note means a person correction. TextRewrite names who rewrote the headline or the summary of a vote or a
/// bill action in a code update that changed nothing else.</summary>
public sealed record PublishedCorrection
{
    public const int MaximumNoteLength = 500;
    public const int MaximumNotedEntries = 20;
    public const int MaximumEntries = MaximumNotedEntries * 3 + 2;

    public required int ReplacedRevisionNumber { get; init; }
    public required DateTimeOffset PublishedAtUtc { get; init; }
    public string? Note { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public PublishedTextRewrite TextRewrite { get; init; }

    [JsonIgnore]
    public bool IsTextRewrite => TextRewrite != PublishedTextRewrite.None;

    // Release files are immutable. Files written before the rewrite kind hold "factsUnchanged": true for a model rewrite, and every vote rewrite then was
    // model text. A read maps the field, and a write never writes it.
    [JsonInclude]
    [JsonPropertyName("factsUnchanged")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    private bool FactsUnchangedOfEarlierFiles
    {
        get => false;
        init
        {
            if (value) TextRewrite = PublishedTextRewrite.Model;
        }
    }

    /// <summary>The rewrite kind of a code update that changed only the text. The origin of the new text names who rewrote it.</summary>
    public static PublishedTextRewrite RewriteBy(PublishedTextOrigin origin) =>
        origin == PublishedTextOrigin.Code ? PublishedTextRewrite.Code : PublishedTextRewrite.Model;

    public static int NotedCount(IEnumerable<PublishedCorrection> corrections) => corrections.Count(item => item.Note is not null);

    public static bool IsAtNoteLimit(IEnumerable<PublishedCorrection> corrections) => NotedCount(corrections) >= MaximumNotedEntries;

    /// <summary>A code update replaces the code update of its kind in the newest run of code updates, so a run holds at most one of each kind. The two
    /// kinds are a source update and a text rewrite. A text rewrite by code replaces a text rewrite by the model, so the entry cap stays.</summary>
    public static PublishedCorrection[] Prepend(PublishedCorrection[] kept, PublishedCorrection entry)
    {
        if (entry.Note is not null) return [entry, .. kept];
        var run = kept.TakeWhile(item => item.Note is null).Count();
        return [entry, .. kept[..run].Where(item => item.IsTextRewrite != entry.IsTextRewrite), .. kept[run..]];
    }

    /// <summary>Checks the correction list of a record of the given kind. A vote can name a text rewrite of either kind. A bill action can name only a code
    /// rewrite, because only code text replaces the model text of a bill with the same facts.</summary>
    public static void ValidateAll(PublishedCorrection[]? corrections, int revisionNumber, PublishedRecordKind kind)
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
            if (!Enum.IsDefined(correction.TextRewrite))
                throw new InvalidDataException("A correction names an unknown text rewrite.");
            if (correction.Note is not null && correction.IsTextRewrite)
                throw new InvalidDataException("Only a code update can name a text rewrite.");
            if (correction.IsTextRewrite && !CanNameTextRewrite(kind, correction.TextRewrite))
                throw new InvalidDataException("Only a vote correction can name a model rewrite, and only a vote or a bill action correction a code rewrite.");
            if (correction.Note is not null) kindsInRun.Clear();
            else if (!kindsInRun.Add(correction.IsTextRewrite))
                throw new InvalidDataException("Between two person corrections the list holds at most one code update of each kind.");
            newerRevision = correction.ReplacedRevisionNumber;
        }
    }

    private static bool CanNameTextRewrite(PublishedRecordKind kind, PublishedTextRewrite rewrite) =>
        kind == PublishedRecordKind.Vote || kind == PublishedRecordKind.Action && rewrite == PublishedTextRewrite.Code;

    public static bool IsNote(string note) =>
        !string.IsNullOrWhiteSpace(note) && note.Length <= MaximumNoteLength && !note.Contains('\0');
}
