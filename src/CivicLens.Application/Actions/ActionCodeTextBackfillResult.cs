namespace CivicLens.Application.Actions;

/// <summary>The counts of one page of <see cref="BackfillActionCodeText"/>. NextCursor names the last examined record, or is null when the page was the last.</summary>
public sealed record ActionCodeTextBackfillResult(int Examined, int Moved, int Skipped, IReadOnlyList<ActionCodeTextSkip> Skips, string? NextCursor);

/// <summary>A published record that kept its text, by record ID and reason. The reason is an action draft code, such as <c>factsChanged</c>.</summary>
public sealed record ActionCodeTextSkip(string RecordId, string Reason)
{
    /// <summary>The facts, sponsors, or issue candidates changed since the current revision. The worker drafts that change.</summary>
    public const string FactsChanged = "factsChanged";

    /// <summary>The current revision no longer holds retired text when the record is read again.</summary>
    public const string NotRetiredText = "notRetiredText";

    /// <summary>The draft entry of the candidate waits for the worker or runs now, so the worker writes the next revision.</summary>
    public const string DraftPending = "draftPending";
}
