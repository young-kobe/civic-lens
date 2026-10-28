using System.Collections.Immutable;
using CivicLens.Core.Relationships;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// The statements, sponsored bills, and substantive cast votes of one official on each tracked issue in one Congress, with the missed votes
/// and the votes with their party. A statement or a sponsorship is in the Congress of its day. An action entry names only its sponsors, so a cosponsor never counts.
/// </summary>
public sealed record TalkAndActionProfile(TalkAndActionColumn Statements, ImmutableArray<PublishedRecordEntry> SponsoredBills,
    TalkAndActionColumn Votes, ImmutableArray<MissedVotes> MissedByChamber, ImmutableArray<VotesWithParty> WithPartyByChamber)
{
    public const int MinimumRecordsForShare = 5;
    public const string StatementCount = "statement";
    public const string BillCount = "bill";
    public const string VoteCount = "vote";

    public ImmutableArray<string> IssueIds => [.. new[] { Statements, Votes }.SelectMany(column => column.RecordIdsByIssue.Keys)
        .Concat(SponsoredBills.SelectMany(bill => bill.IssueIds)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    public ImmutableArray<PublishedRecordEntry> SponsoredOn(string issueId) => [.. SponsoredBills.Where(bill => bill.IssueIds.Contains(issueId))];

    public static string CountKey(string countName, string issueId) => $"{countName}:{issueId}";

    public ILookup<string, string> CountKeysByRecord() =>
        Statements.Counted().Select(pair => (pair.RecordId, Key: CountKey(StatementCount, pair.IssueId)))
            .Concat(SponsoredBills.SelectMany(bill => bill.IssueIds.Select(issueId => (bill.RecordId, Key: CountKey(BillCount, issueId)))))
            .Concat(Votes.Counted().Select(pair => (pair.RecordId, Key: CountKey(VoteCount, pair.IssueId))))
            .ToLookup(pair => pair.RecordId, pair => pair.Key, StringComparer.Ordinal);

    public static TalkAndActionProfile Of(int congress, IEnumerable<PublishedRecordEntry> entries, IEnumerable<PublishedOfficialVote> votes)
    {
        var inCongress = entries.Where(entry => CongressCalendar.CongressOn(ReleaseRecordOrder.Day(entry)) == congress).ToLookup(entry => entry.Kind);
        var allVotes = votes.ToArray();
        return new(TalkAndActionColumn.Of(inCongress[PublishedRecordKind.Statement].Select(entry => (entry.RecordId, entry.IssueIds))),
            [.. inCongress[PublishedRecordKind.Action]],
            TalkAndActionColumn.Of(allVotes.Where(vote => vote.Kind.IsSubstantive() && vote.IsCast).Select(vote => (vote.RecordId, vote.IssueIds))),
            MissedVotes.Of(allVotes), VotesWithParty.Of(allVotes));
    }

    public static double? ShareOf(int count, int of) => of >= MinimumRecordsForShare ? (double)count / of : null;
}
