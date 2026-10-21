using CivicLens.Application.Roster;
using CivicLens.Application.Votes;
using CivicLens.Core.Votes;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.HeadlineSummaryOutputs;
using static CivicLens.Tests.Fixtures.SenateVoteWorld;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Votes;

public sealed class SenateVoteReaderTests
{
    private static SenateVoteWorld Nomination() => Create(question: "On the Nomination", document: NominationDocument);

    [Fact]
    public async Task TheVoteFileIsReadOnceBecauseTheDetailPageAndTheMembersPageAreTheSameCapture()
    {
        var world = Nomination();

        var read = await world.Reader(Senators()).ReadAsync(world.Evidence(), default);

        Assert.NotNull(read.Snapshot);
        Assert.Equal(1, world.Stores.BodyReads);
    }

    [Fact]
    public async Task TrackedSenatorsMatchByLisIdAndEachCastCitesItsMemberLineOfTheSavedText()
    {
        var world = Nomination();

        var snapshot = (await world.Reader(Senators()).ReadAsync(world.Evidence(), default)).Snapshot!;

        Assert.Equal([(Alsobrooks, "Yea"), (Banks, "Not Voting")], snapshot.Members.Select(vote => (vote.BioguideId, vote.Cast)));
        Assert.Equal("Member: S428, Angela Alsobrooks, D, MD, Yea", world.Quote(snapshot.Members[0].Citation));
        Assert.Equal((Chamber.Senate, new DateOnly(2025, 1, 9)), (snapshot.Chamber, snapshot.Day));
    }

    [Fact]
    public async Task ATrackedPersonWhoIsNoSenatorIsSkippedBecauseOnlyASenatorCanCastASenateVote()
    {
        var world = Nomination();

        var snapshot = (await world.Reader(new SenatorLisId(Alsobrooks, AlsobrooksLis)).ReadAsync(world.Evidence(), default)).Snapshot!;

        Assert.Equal([Alsobrooks], snapshot.Members.Select(vote => vote.BioguideId));
    }

    [Fact]
    public async Task ASenatorWithTwoLisIdsBlocksTheDraftBecauseTheRosterCannotSayWhichIdCastTheVote()
    {
        var world = Nomination();

        var read = await world.Reader(new SenatorLisId(Alsobrooks, AlsobrooksLis), new SenatorLisId(Alsobrooks, "S999"), new SenatorLisId(Banks, BanksLis))
            .ReadAsync(world.Evidence(), default);

        Assert.Equal((null, VoteDraftErrorCodes.EvidenceUnavailable), (read.Snapshot, read.ErrorCode));
    }

    [Fact]
    public async Task ATrackedSenatorWithoutALisIdBlocksTheDraftBecauseHisVoteWouldBeLostWithoutNotice()
    {
        var world = Nomination();

        var read = await world.Reader(new SenatorLisId(Alsobrooks, null), new SenatorLisId(Banks, BanksLis)).ReadAsync(world.Evidence(), default);

        Assert.Equal((null, VoteDraftErrorCodes.EvidenceUnavailable), (read.Snapshot, read.ErrorCode));
    }

    [Fact]
    public async Task ASavedExtractionOfAnotherParserVersionBlocksBeforeTheCaptureIsRead()
    {
        var world = Nomination();

        var read = await world.Reader(Senators()).ReadAsync(world.Evidence("senate-vote-v0"), default);

        Assert.Equal((null, VoteDraftErrorCodes.ExtractionVersionChanged), (read.Snapshot, read.ErrorCode));
        Assert.Equal(0, world.Stores.BodyReads);
    }

    [Fact]
    public async Task ANominationCitesItsQuestionTextTitleAndDocumentAsFactsAndHasNoBillOrPolicyArea()
    {
        var world = Nomination();

        var snapshot = (await world.Reader(Senators()).ReadAsync(world.Evidence(), default)).Snapshot!;

        Assert.Equal(VoteKind.Nomination, snapshot.Kind);
        Assert.Equal("Question: On the Nomination", world.Quote(snapshot.KindCitation!));
        Assert.Equal(["Started", "Question", "Question text", "Vote title", "Result", "Document", "Count"],
            snapshot.Facts.Select(fact => fact.Text[..fact.Text.IndexOf(':', StringComparison.Ordinal)]));
        Assert.Contains(snapshot.Facts, fact => fact.Text == "Document: PN373");
        Assert.Null(snapshot.PolicyArea);
    }

    [Fact]
    public async Task ABillVoteAddsTheTitleAndPolicyAreaOfItsBillAfterTheVoteFacts()
    {
        var world = Create();

        var snapshot = (await world.Reader(Senators()).ReadAsync(world.Evidence(), default)).Snapshot!;

        Assert.Equal(VoteKind.Cloture, snapshot.Kind);
        Assert.Equal(["Title: A bill on custody", "Policy area: " + BillGateFixtures.TrackedPolicyArea], snapshot.Facts.TakeLast(2).Select(fact => fact.Text));
        Assert.Equal(BillGateFixtures.TrackedPolicyArea, snapshot.PolicyArea);
        Assert.Equal(2, world.Stores.BodyReads);
    }

    [Fact]
    public async Task ANominationDraftsAndCitesWithNoBillBecauseNoBillTitleCanBeMissing()
    {
        var world = Nomination();
        var store = new FakeVoteDraftStore(world.Evidence());
        var model = new FakeHeadlineSummaryModel().Draft(Output(headline: "The Senate voted on a nomination", summary: "The Senate decided on a nominee."));

        await AnalysisWorkers.VoteDrafting(store, new FakeAnalysisStatusStore(), world.Reader(Senators()), model,
            new VoteDraftSettings(10_000_000, concurrency: 1), BillGateFixtures.Revision(), TimeProvider.System).ExecuteAsync(once: true);

        var revision = Assert.Single(store.Revisions);
        Assert.Equal((Chamber.Senate, VoteKind.Nomination, null, null), (revision.Chamber, revision.Kind, revision.BillType, revision.BillNumber));
        Assert.Equal((State.Succeeded, null), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Contains("United States Senate", Assert.Single(model.Requests).SystemPrompt, StringComparison.Ordinal);
    }
}
