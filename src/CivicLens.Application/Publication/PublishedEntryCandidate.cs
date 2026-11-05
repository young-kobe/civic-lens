using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>One revision range of the state table, whether the entry table holds the entry of that revision, and whether an action row lacks its bill line.</summary>
public sealed record PublishedEntryCandidate(PublishedRecordKind Kind, string RecordId, int RevisionNumber, int FirstRelease, int? LastRelease,
    bool HasEntry, bool LacksBillLine)
{
    public bool NeedsBackfill => !HasEntry || LacksBillLine;

    public bool IsOpen => LastRelease is null;

    public bool Covers(int releaseNumber) => releaseNumber >= FirstRelease && (LastRelease is null || releaseNumber <= LastRelease);
}
