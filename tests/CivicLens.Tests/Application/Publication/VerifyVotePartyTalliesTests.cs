using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

/// <summary>
/// The verifier compares the stored groups with the party tallies of the kept vote file. The fixture vote has one tracked Democrat and one untracked
/// Republican, as a real House roll call does, so a group that only the full roll proves is part of every exact case.
/// </summary>
public sealed class VerifyVotePartyTalliesTests
{
    private static readonly KeptReleaseRange Open = new(1, null);

    [Fact]
    public async Task TheFileOfTheFixtureTalliesTheUntrackedMemberThatNoMemberListRowHolds()
    {
        var (_, vote) = await PublishAsync();

        Assert.Single(vote.Members);
        Assert.Equal(["Democrat", "Republican"], vote.PartyTallies!.Select(tally => tally.Party));
    }

    [Fact]
    public async Task StoredGroupsThatEqualTheTalliesOfTheKeptFileVerifyIncludingTheGroupOfAnUntrackedMember()
    {
        var result = await RunAsync(vote => [Evidence(vote, vote.PartyTallies!.Length, vote.PartyTallies)]);

        Assert.Equal((1, 1, 0, 0, 0, 0), (result.Examined, result.Verified, result.Unavailable, result.Unresolved, result.OpenWithoutKeptFile + result.ClosedWithoutKeptFile, result.Failed));
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task AStoredRowForAPartyThatTheFileDoesNotTallyIsReportedAsExtra()
    {
        var result = await RunAsync(vote => [Evidence(vote, 3, [.. vote.PartyTallies!, Tally("Forged", yea: 9)])]);

        Assert.Equal(1, result.Failed);
        Assert.Equal([VotePartyTallyFailure.ExtraGroup, VotePartyTallyFailure.MarkerDiffersFromFile],
            result.Failures.Select(failure => failure.Reason).Order(StringComparer.Ordinal));
        Assert.StartsWith("Forged:", result.Failures.Single(failure => failure.Reason == VotePartyTallyFailure.ExtraGroup).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGroupThatTheFileTalliesButNoRowStoresIsReportedAsMissing()
    {
        var result = await RunAsync(vote => [Evidence(vote, 2, [vote.PartyTallies![0]])]);

        Assert.Equal(VotePartyTallyFailure.MissingGroup, result.Failures.Single(failure => failure.Detail.StartsWith("Republican:", StringComparison.Ordinal)).Reason);
        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public async Task AGroupWithOneChangedCountIsReportedWithTheStoredAndTheFileCounts()
    {
        var result = await RunAsync(vote => [Evidence(vote, 2, [vote.PartyTallies![0] with { Yea = 3 }, vote.PartyTallies[1]])]);

        var failure = Assert.Single(result.Failures);
        Assert.Equal(VotePartyTallyFailure.DifferentCounts, failure.Reason);
        Assert.Contains("stored yea 3", failure.Detail, StringComparison.Ordinal);
        Assert.Contains("file yea 1", failure.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMarkerThatDiffersFromTheStoredRowCountIsReportedEvenWhenTheRowsAreExact()
    {
        var result = await RunAsync(vote => [Evidence(vote, 3, vote.PartyTallies)]);

        Assert.Contains(VotePartyTallyFailure.MarkerWithoutRows, result.Failures.Select(failure => failure.Reason));
    }

    [Fact]
    public async Task ARevisionWithNoMarkerStaysUnresolvedAndIsNeverMadeIntoACount()
    {
        var result = await RunAsync(vote => [Evidence(vote, null, [])]);

        Assert.Equal((1, 0, 0, 1, 0, 0), (result.Examined, result.Verified, result.Unavailable, result.Unresolved, result.OpenWithoutKeptFile + result.ClosedWithoutKeptFile, result.Failed));
    }

    [Fact]
    public async Task RowsOnARevisionWithNoMarkerAreReportedBecauseTheCountIsStillUnknown()
    {
        var result = await RunAsync(vote => [Evidence(vote, null, vote.PartyTallies)]);

        Assert.Equal(VotePartyTallyFailure.RowsWithoutMarker, Assert.Single(result.Failures).Reason);
        Assert.Equal(0, result.Unresolved);
    }

    [Fact]
    public async Task AZeroMarkerOnAClosedRangeWithNoKeptFileIsUnavailable()
    {
        var result = await RunAsync(vote => [Evidence(vote, 0, [], releases: new(2, 3))]);

        Assert.Equal((1, 0, 1, 0, 0, 0), (result.Examined, result.Verified, result.Unavailable, result.Unresolved, result.OpenWithoutKeptFile + result.ClosedWithoutKeptFile, result.Failed));
    }

    [Fact]
    public async Task AZeroMarkerIsReportedWhenTheKeptFileHoldsCompleteTallies()
    {
        var result = await RunAsync(vote => [Evidence(vote, 0, [])]);

        Assert.Equal(VotePartyTallyFailure.MarkerDiffersFromFile, Assert.Single(result.Failures).Reason);
    }

    [Fact]
    public async Task RowsOnAZeroMarkerRevisionAreReportedOneByOne()
    {
        var result = await RunAsync(vote => [Evidence(vote, 0, vote.PartyTallies, releases: new(2, 3))]);

        Assert.Equal(2, result.Failures.Count);
        Assert.All(result.Failures, failure => Assert.Equal(VotePartyTallyFailure.ExtraGroup, failure.Reason));
        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public async Task AMarkerOnARevisionThatNoRecordStateNamesFailsBecauseNoWriterSetsOne()
    {
        var positive = await RunAsync(vote => [Evidence(vote, 2, vote.PartyTallies) with { Releases = null }]);
        var zero = await RunAsync(vote => [Evidence(vote, 0, []) with { Releases = null }]);

        Assert.Equal(VotePartyTallyFailure.MarkerWithoutState, Assert.Single(positive.Failures).Reason);
        Assert.Equal(VotePartyTallyFailure.MarkerWithoutState, Assert.Single(zero.Failures).Reason);
        Assert.Equal((1, 1), (positive.Failed, zero.Failed));
    }

    [Fact]
    public async Task AZeroMarkerOnAnOpenRangeWhoseFileTheActiveFolderLacksIsAnOpenMiss()
    {
        var result = await RunAsync(vote => [Evidence(vote, 0, [], releases: new(2, null))]);

        Assert.Equal((0, 1, 0), (result.Unavailable, result.OpenWithoutKeptFile, result.Failed));
    }

    [Fact]
    public async Task APositiveMarkerWhoseFileNoKeptFolderHoldsStaysUnprovenAndCountsApartForAnOpenAndAClosedRange()
    {
        var openOutside = await RunAsync(vote => [Evidence(vote, 2, vote.PartyTallies, releases: new(2, null))]);
        var closedOutside = await RunAsync(vote => [Evidence(vote, 2, vote.PartyTallies, releases: new(2, 3))]);

        Assert.Equal((0, 1, 0, 0), (openOutside.Verified, openOutside.OpenWithoutKeptFile, openOutside.ClosedWithoutKeptFile, openOutside.Failed));
        Assert.Equal((0, 0, 1, 0), (closedOutside.Verified, closedOutside.OpenWithoutKeptFile, closedOutside.ClosedWithoutKeptFile, closedOutside.Failed));
    }

    [Fact]
    public async Task AClosedRangeWhoseKeptFolderStillHoldsTheFileVerifies()
    {
        var result = await RunAsync(vote => [Evidence(vote, 2, vote.PartyTallies, releases: new(1, 1))]);

        Assert.Equal((1, 0, 0), (result.Verified, result.ClosedWithoutKeptFile, result.Failed));
    }

    [Fact]
    public async Task AFileOfAnotherRevisionDoesNotProveTheGroups()
    {
        var result = await RunAsync(vote => [Evidence(vote, 2, vote.PartyTallies) with { RevisionNumber = vote.RevisionNumber + 1 }]);

        Assert.Equal((0, 1), (result.Verified, result.OpenWithoutKeptFile));
    }

    [Fact]
    public async Task AFailureInOneRevisionDoesNotStopTheNextRevision()
    {
        var result = await RunAsync(vote => [Evidence(vote, 2, [vote.PartyTallies![0]]), Evidence(vote, 2, vote.PartyTallies) with { RevisionNumber = vote.RevisionNumber + 1 }]);

        Assert.Equal((2, 0, 1, 1), (result.Examined, result.Verified, result.OpenWithoutKeptFile, result.Failed));
    }

    [Fact]
    public async Task TheLimitStopsTheScanAndTheCursorContinuesWithTheNextRevision()
    {
        var (scenario, vote) = await PublishAsync();
        scenario.Publications.TallyEvidence.AddRange([Evidence(vote, null, []) with { RecordId = "a" }, Evidence(vote, null, []) with { RecordId = "a", RevisionNumber = 2 },
            Evidence(vote, null, []) with { RecordId = "b" }]);
        var verify = new VerifyVotePartyTallies(scenario.Publications, scenario.Releases);

        var first = await verify.ExecuteAsync(PublicationScenario.Owner, 2, null);
        var second = await verify.ExecuteAsync(PublicationScenario.Owner, 2, VotePartyTallyVerificationCursor.Parse(first.NextCursor));

        Assert.Equal(("2:a", 2, 1), (first.NextCursor, first.Examined, second.Examined));
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task OnlyFiftyFailuresAreListedWhileEveryFailedRevisionIsCounted()
    {
        var (scenario, vote) = await PublishAsync();
        for (var number = 1; number <= 60; number++) scenario.Publications.TallyEvidence.Add(Evidence(vote, null, vote.PartyTallies) with { RevisionNumber = number });

        var result = await new VerifyVotePartyTallies(scenario.Publications, scenario.Releases).ExecuteAsync(PublicationScenario.Owner, 100, null);

        Assert.Equal((60, 50), (result.Failed, result.Failures.Count));
    }

    [Fact]
    public async Task ANonOwnerAndAZeroLimitAreRefused()
    {
        var scenario = new PublicationScenario();
        var verify = new VerifyVotePartyTallies(scenario.Publications, scenario.Releases);

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

    private static async Task<(PublicationScenario Scenario, PublishedVote Vote)> PublishAsync()
    {
        var scenario = new PublicationScenario();
        var recordId = scenario.AddVote('d', totalLines: [PublicationScenario.DefaultPartyTotal, "Party total R: Yea 0, Nay 1, Present 0, Not voting 0"],
            otherMemberLines: ["Member: X1, A, R, AL, Nay"]);
        var release = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", new PublishedRecordReference(PublishedRecordKind.Vote, recordId)));
        var vote = JsonSerializer.Deserialize<PublishedVote>(scenario.Releases.Directories[release.DirectoryName][PublicationProtocol.RecordDataPath(recordId)],
            PublicationProtocol.JsonOptions)!;
        return (scenario, vote);
    }

    private static async Task<VotePartyTallyVerificationResult> RunAsync(Func<PublishedVote, VotePartyTallyEvidence[]> evidence)
    {
        var (scenario, vote) = await PublishAsync();
        scenario.Publications.TallyEvidence.AddRange(evidence(vote));
        return await new VerifyVotePartyTallies(scenario.Publications, scenario.Releases).ExecuteAsync(PublicationScenario.Owner, 100, null);
    }

    private static VotePartyTallyEvidence Evidence(PublishedVote vote, int? marker, IEnumerable<PublishedPartyTally>? stored, KeptReleaseRange? releases = null) =>
        new(vote.RecordId, vote.RevisionNumber, marker, [.. stored ?? []], releases ?? Open);

    private static PublishedPartyTally Tally(string party, int yea = 0, int nay = 0, int present = 0, int notVoting = 0, int other = 0) =>
        new() { Party = party, Yea = yea, Nay = nay, Present = present, NotVoting = notVoting, Other = other };
}
