using CivicLens.Application.Collection.VoteviewVotes;
using CivicLens.Application.Votes;
using CivicLens.Core.Votes;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.VoteviewVoteWorld;

namespace CivicLens.Tests.Application.Votes;

public sealed class VoteviewVoteReaderTests
{
    [Fact]
    public async Task ATrackedSenatorMatchesByBioguideIdAndHisCastCitesAMemberLineWhoseNameHoldsCommas()
    {
        var world = Create();

        var snapshot = (await world.Reader().ReadAsync(world.Evidence(), default)).Snapshot!;

        Assert.Equal([(Coons, "Nay"), (King, "Nay")], snapshot.Members.Select(vote => (vote.BioguideId, vote.Cast)));
        Assert.Equal("Member: K000383, KING, Angus Stanley, Jr., I, ME, Nay", world.Quote(snapshot.Members[1].Citation));
        Assert.Equal((Chamber.Senate, VoteSource.Voteview, new DateOnly(2025, 2, 20)), (snapshot.Chamber, snapshot.Source, snapshot.Day));
    }

    [Fact]
    public async Task TheKindAndTheFactsComeFromTheQuestionAndTheSenateFactLinesSoAVoteviewVoteIsClassifiedLikeASenateGovVote()
    {
        var world = Create();

        var snapshot = (await world.Reader().ReadAsync(world.Evidence(), default)).Snapshot!;

        Assert.Equal(VoteKind.Cloture, snapshot.Kind);
        Assert.Equal("Question: On the Cloture Motion", world.Quote(snapshot.KindCitation!));
        Assert.Equal(["Started: 2025-02-20", "Question: On the Cloture Motion", "Vote title: Kashyap Patel, of Nevada, to be Director of the Federal Bureau of Investigation for a term of ten years",
            "Result: Cloture Motion Agreed to", "Count: Yea 2, Nay 4, Present 0, Not voting 0"], snapshot.Facts.Select(fact => fact.Text));
    }

    [Fact]
    public async Task TheReaderLoadsTheRollCallFileTheMemberFileAndTheVoteFileBecauseTheSavedTextIsTheRenderOfAllThree()
    {
        var world = Create();

        await world.Reader().ReadAsync(world.Evidence(), default);

        Assert.Equal(3, world.Stores.BodyReads);
    }

    [Fact]
    public async Task ASavedTextThatDiffersFromTheRenderOfItsCapturesBlocksTheDraft()
    {
        var world = Create();

        var read = await world.Reader().ReadAsync(world.Evidence(world.Text.Replace("Nay", "Yea", StringComparison.Ordinal)), default);

        Assert.Equal((null, VoteDraftErrorCodes.RenderedTextMismatch), (read.Snapshot, read.ErrorCode));
    }

    [Fact]
    public async Task AnotherExtractionVersionBlocksBeforeAnyCaptureIsRead()
    {
        var world = Create();

        var read = await world.Reader().ReadAsync(world.Evidence(extractionVersion: "voteview-senate-vote-v0"), default);

        Assert.Equal((null, VoteDraftErrorCodes.ExtractionVersionChanged), (read.Snapshot, read.ErrorCode));
        Assert.Equal(0, world.Stores.BodyReads);
    }
}
