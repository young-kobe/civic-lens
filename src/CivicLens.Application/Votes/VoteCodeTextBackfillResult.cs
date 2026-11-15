namespace CivicLens.Application.Votes;

/// <summary>The counts of one page of <see cref="BackfillVoteCodeText"/>. NextCursor names the last examined record, or is null when the page was the last.</summary>
public sealed record VoteCodeTextBackfillResult(int Examined, int Moved, int Skipped, IReadOnlyList<VoteCodeTextSkip> Skips, string? NextCursor);

/// <summary>A published vote that kept its model text, by record ID and reason. The reason is a vote draft code, such as <c>factsChanged</c>.</summary>
public sealed record VoteCodeTextSkip(string RecordId, string Reason)
{
    /// <summary>The facts, members, kind, or issue candidates changed since the current revision. The worker drafts that change.</summary>
    public const string FactsChanged = "factsChanged";

    /// <summary>The current revision no longer holds model text when the vote is read again.</summary>
    public const string NotModelText = "notModelText";

    /// <summary>The draft entry of the candidate waits for the worker or runs now, so the worker writes the next revision.</summary>
    public const string DraftPending = "draftPending";
}
