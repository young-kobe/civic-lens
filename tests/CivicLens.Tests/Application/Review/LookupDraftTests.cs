using System.Globalization;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Application.Actions;
using CivicLens.Tests.Application.Votes;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Review;

public sealed class LookupDraftTests
{
    private static readonly ReviewActor Reviewer = new("auth0|reviewer", ReviewRole.Reviewer);
    private const string ActionRecord = "action-record";
    private const string VoteRecord = "vote-record";
    private readonly FakeActionReviewStore actions = new();
    private readonly FakeVoteReviewStore votes = new();
    private readonly FakeClock clock = new(new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero));

    private readonly FakeRecordKindStore kinds = new();

    private LookupDraft Lookup() => new(actions, votes, kinds, clock);

    [Theory]
    [InlineData("HR 1234")]
    [InlineData("H.R. 1234")]
    [InlineData("  HR1234 ")]
    [InlineData("119 HR 1234")]
    public async Task ABillWithoutACongressMeansTheCurrentCongressAndEveryCitationSpellingFindsTheSameActionDraft(string query)
    {
        actions.Bills[(119, "hr", 1234)] = ActionRecord;

        var hit = await Lookup().ExecuteAsync(Reviewer, query);

        Assert.Equal(new DraftLookupHit(PublishedRecordKind.Action, ActionRecord), hit);
    }

    [Fact]
    public async Task ABillOfAnEarlierCongressIsFoundOnlyWhenTheQueryNamesThatCongress()
    {
        actions.Bills[(118, "hjres", 5)] = ActionRecord;

        Assert.Null(await Lookup().ExecuteAsync(Reviewer, "HJ Res 5"));
        Assert.Equal(PublishedRecordKind.Action, (await Lookup().ExecuteAsync(Reviewer, "118 H.J.Res. 5"))!.Kind);
    }

    [Fact]
    public async Task ARollCallMeansAHouseRollOfTheCurrentCongressAndSessionBecauseRollNumbersRestartEachSessionAndEachChamber()
    {
        votes.Rolls[(Chamber.House, 119, 2, 245)] = VoteRecord;
        votes.Rolls[(Chamber.House, 119, 1, 246)] = "first-session";
        votes.Rolls[(Chamber.Senate, 119, 2, 247)] = "senate-vote";

        Assert.Equal(new DraftLookupHit(PublishedRecordKind.Vote, VoteRecord), await Lookup().ExecuteAsync(Reviewer, "Roll 245"));
        Assert.Null(await Lookup().ExecuteAsync(Reviewer, "roll 246"));
        Assert.Null(await Lookup().ExecuteAsync(Reviewer, "roll 247"));
    }

    [Theory]
    [InlineData("Senate roll 247")]
    [InlineData("senate Roll247")]
    public async Task ASenateRollCallMeansASenateRollOfTheCurrentCongressAndSessionBecauseTheTwoChambersNumberTheirRollsApart(string query)
    {
        votes.Rolls[(Chamber.House, 119, 2, 247)] = "house-vote";
        votes.Rolls[(Chamber.Senate, 119, 2, 247)] = "senate-vote";

        Assert.Equal(new DraftLookupHit(PublishedRecordKind.Vote, "senate-vote"), await Lookup().ExecuteAsync(Reviewer, query));
    }

    [Theory]
    [InlineData(PublishedRecordKind.Statement)]
    [InlineData(PublishedRecordKind.Action)]
    [InlineData(PublishedRecordKind.Vote)]
    [InlineData(PublishedRecordKind.Brief)]
    [InlineData(PublishedRecordKind.Relationship)]
    [InlineData(PublishedRecordKind.ExecutiveAction)]
    public async Task ARecordIdOpensTheEditorOfTheKindThatOwnsIt(PublishedRecordKind kind)
    {
        var recordId = new string('a', 64);
        kinds.Records[recordId] = kind;

        Assert.Equal(new DraftLookupHit(kind, recordId), await Lookup().ExecuteAsync(Reviewer, $"  {recordId} "));
    }

    [Fact]
    public async Task ADocumentChangeDraftIdIsAValidRecordIdBecauseItIsShorterThanOtherRecordIds()
    {
        var draftId = new string('b', 32);
        kinds.Records[draftId] = PublishedRecordKind.DocumentChange;

        Assert.Equal(new DraftLookupHit(PublishedRecordKind.DocumentChange, draftId), await Lookup().ExecuteAsync(Reviewer, draftId));
        Assert.Null(await Lookup().ExecuteAsync(Reviewer, new string('c', 32)));
    }

    [Theory]
    [InlineData("https://review.example.test/Review/Actions/{0}")]
    [InlineData("https://review.example.test/Review/Actions/{0}?status=saved")]
    [InlineData("  https://review.example.test/Review/Actions/{0}/  ")]
    [InlineData("https://civic-lens.info/records/{0}.html")]
    public async Task AnEditorLinkOrAPublicRecordLinkGivesItsRecordIdSoACopiedAddressWorks(string link)
    {
        var recordId = new string('d', 64);
        kinds.Records[recordId] = PublishedRecordKind.Action;

        Assert.Equal(new DraftLookupHit(PublishedRecordKind.Action, recordId),
            await Lookup().ExecuteAsync(Reviewer, string.Format(CultureInfo.InvariantCulture, link, recordId)));
    }

    [Theory]
    [InlineData("https://example.test/")]
    [InlineData("https://example.test/news/story.html")]
    [InlineData("https://example.test/Review/Actions/abc")]
    [InlineData("ftp://example.test/records/dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd.html")]
    public async Task ALinkWithoutARecordIdIsAUserErrorBeforeAnyRead(string link)
    {
        await Assert.ThrowsAsync<DraftLookupFormatException>(() => Lookup().ExecuteAsync(Reviewer, link));

        Assert.Equal(0, kinds.Lookups);
    }

    [Fact]
    public async Task ARecordIdWithNoRecordIsNotAnError()
    {
        Assert.Null(await Lookup().ExecuteAsync(Reviewer, new string('c', 64)));
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
    [InlineData("Senate Roll 0")]
    [InlineData("House roll 5")]
    [InlineData("HR 12; DROP TABLE")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789")]
    [InlineData("0123456789abcdef0123456789abcdef01234567")]
    public async Task AnInputThatNamesNoBillRollCallOrRecordIdIsAUserErrorBeforeAnyRead(string? query)
    {
        await Assert.ThrowsAsync<DraftLookupFormatException>(() => Lookup().ExecuteAsync(Reviewer, query));

        Assert.Equal((0, 0, 0), (actions.BillLookups, votes.RollLookups, kinds.Lookups));
    }

    [Fact]
    public async Task AClockDateWithNoCongressIsAnOperationalFaultNotAUserError()
    {
        clock.Now = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Lookup().ExecuteAsync(Reviewer, "HR 1"));
    }

    [Fact]
    public async Task OnlyAReviewerMayLookUpADraft()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Lookup().ExecuteAsync(new ReviewActor("auth0|nobody", (ReviewRole)99), "HR 1"));
    }

    private sealed class FakeRecordKindStore : IRecordKindStore
    {
        public Dictionary<string, PublishedRecordKind> Records { get; } = [];
        public int Lookups { get; private set; }

        public Task<PublishedRecordKind?> FindKindAsync(string recordId, CancellationToken cancellationToken)
        {
            Lookups++;
            return Task.FromResult(Records.TryGetValue(recordId, out var kind) ? kind : (PublishedRecordKind?)null);
        }
    }
}
