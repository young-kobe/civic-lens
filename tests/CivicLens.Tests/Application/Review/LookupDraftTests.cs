using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Application.Votes;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Review;

public sealed class LookupDraftTests
{
    private static readonly ReviewActor Reviewer = new("auth0|reviewer", ReviewRole.Reviewer);
    private const string ActionRecord = "action-record";
    private const string VoteRecord = "vote-record";
    private readonly FakeVoteReviewStore votes = new();
    private readonly FakeClock clock = new(new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero));

    private LookupDraft Lookup() => new(votes, clock);

    [Theory]
    [InlineData("HR 1234")]
    [InlineData("H.R. 1234")]
    [InlineData("  HR1234 ")]
    [InlineData("119 HR 1234")]
    public async Task ABillWithoutACongressMeansTheCurrentCongressAndEveryCitationSpellingFindsTheSameActionDraft(string query)
    {
        votes.BillActions[(119, "hr", 1234)] = [ActionRecord];

        var hit = await Lookup().ExecuteAsync(Reviewer, query);

        Assert.Equal(new DraftLookupHit(PublishedRecordKind.Action, ActionRecord), hit);
    }

    [Fact]
    public async Task ABillOfAnEarlierCongressIsFoundOnlyWhenTheQueryNamesThatCongress()
    {
        votes.BillActions[(118, "hjres", 5)] = [ActionRecord];

        Assert.Null(await Lookup().ExecuteAsync(Reviewer, "HJ Res 5"));
        Assert.Equal(PublishedRecordKind.Action, (await Lookup().ExecuteAsync(Reviewer, "118 H.J.Res. 5"))!.Kind);
    }

    [Fact]
    public async Task ARollCallMeansTheCurrentCongressAndSessionBecauseRollNumbersRestartEachSession()
    {
        votes.Rolls[(119, 2, 245)] = VoteRecord;
        votes.Rolls[(119, 1, 246)] = "first-session";

        Assert.Equal(new DraftLookupHit(PublishedRecordKind.Vote, VoteRecord), await Lookup().ExecuteAsync(Reviewer, "Roll 245"));
        Assert.Null(await Lookup().ExecuteAsync(Reviewer, "roll 246"));
    }

    [Fact]
    public async Task ABillWithNoDraftIsNotAnError()
    {
        Assert.Null(await Lookup().ExecuteAsync(Reviewer, "HR 99"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1234")]
    [InlineData("HR")]
    [InlineData("HR 0")]
    [InlineData("HR 123456")]
    [InlineData("XX 12")]
    [InlineData("0 HR 12")]
    [InlineData("118 Roll 245")]
    [InlineData("Roll 0")]
    [InlineData("HR 12; DROP TABLE")]
    public async Task AnInputThatNamesNoBillOrRollCallIsAUserErrorBeforeAnyRead(string? query)
    {
        await Assert.ThrowsAsync<DraftLookupFormatException>(() => Lookup().ExecuteAsync(Reviewer, query));

        Assert.Equal(0, votes.BillLookups);
    }

    [Fact]
    public async Task OnlyAReviewerMayLookUpADraft()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Lookup().ExecuteAsync(new ReviewActor("auth0|nobody", (ReviewRole)99), "HR 1"));
    }
}
