using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class VerifyVotePartyTalliesTests
{
    private static readonly VoteMemberCast[] Members =
    [
        new("Democrat", "Yea"), new("Democrat", "Aye"), new("Democrat", "Nay"), new("Republican", "No"), new("Republican", "Not Voting"), new("Republican", "Smith")
    ];

    private static readonly PublishedPartyTally[] Exact =
    [
        Tally("Democrat", yea: 2, nay: 1),
        Tally("Republican", nay: 1, notVoting: 1, other: 1)
    ];

    [Fact]
    public async Task StoredGroupsThatEqualTheGroupsRebuiltFromTheMemberListVerify()
    {
        var result = await RunAsync(Evidence("a", 1, 2, Exact));

        Assert.Equal((1, 1, 0, 0, 0), (result.Examined, result.Verified, result.Unavailable, result.Unresolved, result.Failed));
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task AStoredRowForAPartyWithNoMemberIsReportedAsExtra()
    {
        var result = await RunAsync(Evidence("a", 1, 3, [.. Exact, Tally("Forged", yea: 9)]));

        var failure = Assert.Single(result.Failures);
        Assert.Equal((1, VotePartyTallyFailure.ExtraGroup), (result.Failed, failure.Reason));
        Assert.StartsWith("Forged:", failure.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGroupThatTheMemberListHoldsButNoRowStoresIsReportedAsMissing()
    {
        var result = await RunAsync(Evidence("a", 1, 2, [Exact[0]]));

        Assert.Equal(VotePartyTallyFailure.MissingGroup, result.Failures.Single(failure => failure.Detail.StartsWith("Republican:", StringComparison.Ordinal)).Reason);
        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public async Task AGroupWithOneChangedCountIsReportedWithBothCountSets()
    {
        var result = await RunAsync(Evidence("a", 1, 2, [Exact[0] with { Yea = 3 }, Exact[1]]));

        var failure = Assert.Single(result.Failures);
        Assert.Equal(VotePartyTallyFailure.DifferentCounts, failure.Reason);
        Assert.Contains("stored yea 3", failure.Detail, StringComparison.Ordinal);
        Assert.Contains("rebuilt yea 2", failure.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMarkerThatDiffersFromTheStoredRowCountIsReportedEvenWhenTheRowsAreExact()
    {
        var result = await RunAsync(Evidence("a", 1, 3, Exact));

        Assert.Equal(VotePartyTallyFailure.MarkerWithoutRows, Assert.Single(result.Failures).Reason);
    }

    [Fact]
    public async Task ARevisionWithNoMarkerStaysUnresolvedAndIsNeverRebuiltIntoACount()
    {
        var result = await RunAsync(Evidence("a", 1, null, []));

        Assert.Equal((1, 0, 0, 1, 0), (result.Examined, result.Verified, result.Unavailable, result.Unresolved, result.Failed));
    }

    [Fact]
    public async Task RowsOnARevisionWithNoMarkerAreReportedBecauseTheCountIsStillUnknown()
    {
        var result = await RunAsync(Evidence("a", 1, null, Exact));

        Assert.Equal(VotePartyTallyFailure.RowsWithoutMarker, Assert.Single(result.Failures).Reason);
        Assert.Equal(0, result.Unresolved);
    }

    [Fact]
    public async Task AZeroMarkerIsUnavailableAndIsNeverRebuiltFromTheMemberListEvenWhenTheListIsComplete()
    {
        var result = await RunAsync(Evidence("a", 1, 0, []));

        Assert.Equal((1, 0, 1, 0, 0), (result.Examined, result.Verified, result.Unavailable, result.Unresolved, result.Failed));
    }

    [Fact]
    public async Task RowsOnAZeroMarkerRevisionAreReportedOneByOne()
    {
        var result = await RunAsync(Evidence("a", 1, 0, Exact));

        Assert.Equal(2, result.Failures.Count);
        Assert.All(result.Failures, failure => Assert.Equal(VotePartyTallyFailure.ExtraGroup, failure.Reason));
        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public async Task AMemberWithoutAPartyMakesTheRebuildImpossibleAndIsReported()
    {
        var evidence = Evidence("a", 1, 2, Exact) with { Members = [.. Members.Take(5), new VoteMemberCast(null, "Yea")] };

        var result = await RunAsync(evidence);

        Assert.Equal(VotePartyTallyFailure.MemberPartyMissing, Assert.Single(result.Failures).Reason);
    }

    [Fact]
    public async Task AFailureInOneRevisionDoesNotStopTheNextRevision()
    {
        var result = await RunAsync(Evidence("a", 1, 2, [Exact[0]]), Evidence("b", 1, 2, Exact));

        Assert.Equal((2, 1, 1), (result.Examined, result.Verified, result.Failed));
    }

    [Fact]
    public async Task TheLimitStopsTheScanAndTheCursorContinuesWithTheNextRevision()
    {
        var scenario = new PublicationScenario();
        scenario.Publications.TallyEvidence.AddRange([Evidence("a", 1, 2, Exact), Evidence("a", 2, 2, Exact), Evidence("b", 1, 2, Exact)]);
        var verify = new VerifyVotePartyTallies(scenario.Publications);

        var first = await verify.ExecuteAsync(PublicationScenario.Owner, 2, null);
        var second = await verify.ExecuteAsync(PublicationScenario.Owner, 2, VotePartyTallyVerificationCursor.Parse(first.NextCursor));

        Assert.Equal(("2:a", 2, 1), (first.NextCursor, first.Examined, second.Examined));
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task OnlyFiftyFailuresAreListedWhileEveryFailedRevisionIsCounted()
    {
        var scenario = new PublicationScenario();
        for (var number = 1; number <= 60; number++) scenario.Publications.TallyEvidence.Add(Evidence("a", number, 2, [Exact[0]]));

        var result = await new VerifyVotePartyTallies(scenario.Publications).ExecuteAsync(PublicationScenario.Owner, 100, null);

        Assert.Equal((60, 50), (result.Failed, result.Failures.Count));
    }

    [Fact]
    public async Task ANonOwnerAndAZeroLimitAreRefused()
    {
        var verify = new VerifyVotePartyTallies(new PublicationScenario().Publications);

        await Assert.ThrowsAnyAsync<Exception>(() => verify.ExecuteAsync(PublicationScenario.Reviewer, 1, null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => verify.ExecuteAsync(PublicationScenario.Owner, 0, null));
    }

    [Theory]
    [InlineData("1:vote:abc")]
    [InlineData("12:a")]
    public void ACursorRoundTripsAndKeepsAColonOfTheRecordId(string text) =>
        Assert.Equal(text, VotePartyTallyVerificationCursor.Parse(text)!.Encode());

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("0:a")]
    [InlineData("-1:a")]
    [InlineData("01:a")]
    [InlineData("1:")]
    public void ABadCursorIsRefused(string text) =>
        Assert.Throws<ArgumentException>(() => VotePartyTallyVerificationCursor.Parse(text));

    private static async Task<VotePartyTallyVerificationResult> RunAsync(params VotePartyTallyEvidence[] evidence)
    {
        var scenario = new PublicationScenario();
        scenario.Publications.TallyEvidence.AddRange(evidence);
        return await new VerifyVotePartyTallies(scenario.Publications).ExecuteAsync(PublicationScenario.Owner, 100, null);
    }

    private static VotePartyTallyEvidence Evidence(string record, int revision, int? marker, PublishedPartyTally[] stored) =>
        new(record, revision, marker, stored, Members);

    private static PublishedPartyTally Tally(string party, int yea = 0, int nay = 0, int present = 0, int notVoting = 0, int other = 0) =>
        new() { Party = party, Yea = yea, Nay = nay, Present = present, NotVoting = notVoting, Other = other };
}
