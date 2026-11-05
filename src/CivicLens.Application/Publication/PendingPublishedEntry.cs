using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// A range that needs the backfill while it reads the kept folders: it has no row, or its action row has no bill line. The first reason that blocks
/// the range stays. The bill line is the one that every kept folder of the range agrees on. A folder from before the line has none.
/// </summary>
internal sealed class PendingPublishedEntry(PublishedEntryCandidate candidate)
{
    public PublishedEntryCandidate Candidate { get; } = candidate;

    public PublishedRecordEntry? Entry { get; set; }

    public string? SkipReason { get; private set; }

    public string? BillLine { get; private set; }

    public bool HasRow { get; set; }

    public bool WritesRow => !Candidate.HasEntry;

    public PublishedRecordEntry? EntryToInsert => Entry is null ? null : Entry with { BillLine = BillLine };

    public bool IsReady => Entry is not null && SkipReason is null;

    public bool IsBlocked => SkipReason is not null;

    public void Block(string reason) => SkipReason ??= reason;

    /// <summary>Takes the line of one kept folder. It returns false when the line differs from the line of an earlier folder.</summary>
    public bool TryAddBillLine(string? line)
    {
        if (line is null) return true;
        BillLine ??= line;
        return BillLine == line;
    }
}
