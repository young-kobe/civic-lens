using System.Collections.Immutable;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Votes.Persistence;

namespace CivicLens.Infrastructure.Votes;

/// <summary>Vote revision rows with their child rows, and the member lists that they name. A page costs a fixed number of queries.</summary>
internal sealed record VoteRevisionPage(IReadOnlyList<VoteRevisionRow> Rows, ILookup<(string RecordId, int RevisionNumber), VoteRevisionFactRow> Facts,
    ILookup<(string RecordId, int RevisionNumber), VoteRevisionIssueRow> Issues, IReadOnlyDictionary<string, short> MemberCounts,
    ILookup<string, VoteMemberListEntryRow> Entries)
{
    public IEnumerable<VoteRevisionFactRow> FactsOf(VoteRevisionRow row) => Facts[(row.RecordId, row.RevisionNumber)];

    public IEnumerable<VoteRevisionIssueRow> IssuesOf(VoteRevisionRow row) => Issues[(row.RecordId, row.RevisionNumber)];


    public ImmutableArray<MemberVote> MembersOf(VoteRevisionRow row) => [.. Entries[row.MemberListId!].Select(entry =>
        new MemberVote(entry.PersonId, entry.BioguideId, entry.VoteCast, new(entry.CitationExtractionId, entry.CitationStart, entry.CitationLength)))];

    /// <summary>Rebuilds the revision of a row. A row with null columns or a broken member list fails here, and the JSON is no fallback.</summary>
    public VoteDraftRevision Rebuild(VoteRevisionRow row)
    {
        if (row.Headline is null)
            throw VoteRevisionRows.NotBackfilled($"Vote revision {row.RecordId} {row.RevisionNumber}");
        var members = MembersOf(row);
        if (!MemberCounts.TryGetValue(row.MemberListId!, out var count) || count != members.Length)
            throw new InvalidDataException($"Vote revision {row.RecordId} {row.RevisionNumber} has a member list that is missing or incomplete.");
        return VoteRevisionRows.Rebuild(row, members, FactsOf(row), IssuesOf(row));
    }

    public IReadOnlyList<StoredRevision<VoteRevisionRow, VoteDraftRevision>> ToStoredRevisions() =>
        [.. Rows.Select(row => new StoredRevision<VoteRevisionRow, VoteDraftRevision>(row, () => Rebuild(row)))];
}
