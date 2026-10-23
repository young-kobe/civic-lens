using System.Collections.Immutable;
using CivicLens.Core.Relationships;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// The share of the statements, sponsorships, and substantive cast votes of one official on each tracked issue in one Congress.
/// A statement or a sponsorship is in the Congress of its day. An action entry names only its sponsors, so a cosponsor never counts.
/// </summary>
public sealed record TalkAndActionProfile(TalkAndActionColumn Statements, TalkAndActionColumn Sponsorships,
    TalkAndActionColumn Votes, int NotVotingRolls, int ProceduralVotes)
{
    public const int MinimumRecordsForShare = 5;

    public ImmutableArray<string> IssueIds => [.. new[] { Statements, Sponsorships, Votes }
        .SelectMany(column => column.ByIssue.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    public static TalkAndActionProfile Of(int congress, IEnumerable<PublishedRecordEntry> entries, IEnumerable<PublishedOfficialVote> votes)
    {
        var inCongress = entries.Where(entry => CongressCalendar.CongressOn(ReleaseRecordOrder.Day(entry)) == congress).ToArray();
        var rolls = votes.ToLookup(vote => vote.Kind.IsSubstantive());
        var cast = rolls[true].ToLookup(vote => vote.IsCast);
        return new(ColumnOf(inCongress, PublishedRecordKind.Statement), ColumnOf(inCongress, PublishedRecordKind.Action),
            TalkAndActionColumn.Of(cast[true].Select(vote => vote.IssueIds)), cast[false].Count(), rolls[false].Count());
    }

    private static TalkAndActionColumn ColumnOf(PublishedRecordEntry[] entries, PublishedRecordKind kind) =>
        TalkAndActionColumn.Of(entries.Where(entry => entry.Kind == kind).Select(entry => entry.IssueIds));
}
