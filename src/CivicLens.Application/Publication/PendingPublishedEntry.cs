using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>A range without a row while the backfill reads the kept folders. The first reason that blocks the range stays.</summary>
internal sealed class PendingPublishedEntry(PublishedEntryCandidate candidate)
{
    public PublishedEntryCandidate Candidate { get; } = candidate;

    public PublishedRecordEntry? Entry { get; set; }

    public string? SkipReason { get; private set; }

    public bool HasRow { get; set; }

    public bool IsReady => Entry is not null && SkipReason is null;

    public bool IsBlocked => SkipReason is not null;

    public void Block(string reason) => SkipReason ??= reason;
}
