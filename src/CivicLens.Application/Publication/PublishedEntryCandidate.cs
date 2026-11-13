using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>One revision range of the state table, whether the entry table holds the entry of that revision, and which published fields still need backfill.</summary>
public sealed record PublishedEntryCandidate(PublishedRecordKind Kind, string RecordId, int RevisionNumber, int FirstRelease, int? LastRelease,
    bool HasEntry, bool LacksBillLine, bool LacksApprover = false, bool LacksApprovalTime = false)
{
    public bool NeedsBackfill => !HasEntry || LacksBillLine || LacksApprover || LacksApprovalTime;

    public bool IsOpen => LastRelease is null;

    public bool Covers(int releaseNumber) => releaseNumber >= FirstRelease && (LastRelease is null || releaseNumber <= LastRelease);
}
