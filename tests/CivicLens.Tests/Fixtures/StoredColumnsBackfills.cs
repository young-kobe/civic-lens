using CivicLens.Application.Collection;
using CivicLens.Application.Publication;
using CivicLens.Application.Roster;
using CivicLens.Application.Votes;

namespace CivicLens.Tests.Fixtures;

/// <summary>Builds the stored columns backfill for the tests of its columns. A test of the sort names passes its own rosters.</summary>
internal static class StoredColumnsBackfills
{
    private static readonly CollectionConfiguration NoRosters = new() { People = [], Sources = [] };

    public static BackfillStoredColumns WithoutRosters(IPublicationStore publications, IReleaseDirectory releases, IVoteReviewStore votes) =>
        WithRosters(publications, releases, votes, NoRosters, new FixedMemberRoster([]), new FixedExecutiveRoster([]), new PhotoCaptures());

    public static BackfillStoredColumns WithRosters(IPublicationStore publications, IReleaseDirectory releases, IVoteReviewStore votes,
        CollectionConfiguration configuration, IMemberRosterStore members, IExecutiveRosterStore executives, PhotoCaptures captures) =>
        new(publications, releases, votes, new BackfillRosterLastNames(configuration, members, executives, captures, captures, "artifacts"),
            new PublicationOfficials(configuration, members, executives, new FixedPersonRegistry()));
}
