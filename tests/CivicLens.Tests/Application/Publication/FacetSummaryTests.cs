using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class FacetSummaryTests
{
    [Fact]
    public void ItCountsVotesAndBillsApartAndLeavesOtherKindsOutSoNoCountClaimsMoreThanTheTagsShow()
    {
        var summary = FacetSummary.From([Entry(PublishedRecordKind.Vote, PublishedVoteAlignment.Split), Entry(PublishedRecordKind.Vote, PublishedVoteAlignment.PartiesAgree),
            Entry(PublishedRecordKind.Vote, PublishedVoteAlignment.Mixed), Entry(PublishedRecordKind.Vote, null), Entry(PublishedRecordKind.Action, null),
            Entry(PublishedRecordKind.Statement, null)]);

        Assert.Equal(new FacetSummary(Votes: 4, SplitVotes: 1, PartiesAgreedVotes: 1, Bills: 1), summary);
        Assert.Equal(2, summary.OtherVotes);
    }

    private static PublishedRecordEntry Entry(PublishedRecordKind kind, PublishedVoteAlignment? alignment) => new()
    {
        Kind = kind,
        RecordId = Guid.NewGuid().ToString("N"),
        RevisionNumber = 1,
        Headline = "Headline",
        FirstPublishedAtUtc = DateTimeOffset.UnixEpoch,
        Officials = [],
        IssueIds = ["housing"],
        PolicyAreaIds = [],
        Alignment = alignment
    };
}
