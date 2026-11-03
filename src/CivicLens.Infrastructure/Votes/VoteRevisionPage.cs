using System.Collections.Immutable;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Votes.Persistence;

namespace CivicLens.Infrastructure.Votes;

/// <summary>Vote revision rows with their child rows, and the member lists that they name. A page costs a fixed number of queries.</summary>
internal sealed record VoteRevisionPage(IReadOnlyList<VoteRevisionRow> Rows, IReadOnlyDictionary<string, short> MemberCounts,
    ILookup<string, VoteMemberListEntryRow> Entries)
{
    public ImmutableArray<MemberVote> MembersOf(VoteRevisionRow row) => [.. Entries[row.MemberListId!].Select(entry =>
        new MemberVote(entry.PersonId, entry.BioguideId, entry.VoteCast, new(entry.CitationExtractionId, entry.CitationStart, entry.CitationLength)))];

    /// <summary>Rebuilds the revision of a row. A row with null columns or a broken member list fails here, and the JSON is no fallback.</summary>
    public VoteDraftRevision Rebuild(VoteRevisionRow row)
    {
        if (row.Headline is null)
            throw new InvalidDataException($"Vote revision {row.RecordId} {row.RevisionNumber} has null columns. Run the vote revision backfill.");
        var members = MembersOf(row);
        if (!MemberCounts.TryGetValue(row.MemberListId!, out var count) || count != members.Length)
            throw new InvalidDataException($"Vote revision {row.RecordId} {row.RevisionNumber} has a member list that is missing or incomplete.");
        return VoteRevisionRows.Rebuild(row, members);
    }

    public IReadOnlyList<StoredRevision<VoteRevisionRow, VoteDraftRevision>> ToStoredRevisions() =>
        [.. Rows.Select(row => new StoredRevision<VoteRevisionRow, VoteDraftRevision>(row, () => Rebuild(row)))];
}
