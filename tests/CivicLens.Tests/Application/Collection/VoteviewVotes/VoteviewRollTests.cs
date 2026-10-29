using CivicLens.Application.Collection.VoteviewVotes;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.VoteviewVotes;

public sealed class VoteviewRollTests
{
    private static IReadOnlyList<VoteviewRoll> Join(string? rollCalls = null, string? votes = null, string? members = null) =>
        VoteviewRoll.Join(
            VoteviewParser.ParseRollCalls(VoteviewVoteFixtures.Bytes(rollCalls ?? VoteviewVoteFixtures.DefaultRollCalls), 119),
            VoteviewParser.ParseCasts(VoteviewVoteFixtures.Bytes(votes ?? VoteviewVoteFixtures.DefaultVotes), 119),
            VoteviewParser.ParseMembers(VoteviewVoteFixtures.Bytes(members ?? VoteviewVoteFixtures.DefaultMembers), 119));

    [Fact]
    public void ARollCallRendersTheFactLinesTheVoteReaderCitesAndOneMemberLinePerSenatorWithTheBioguideId()
    {
        var text = VoteviewRenderer.Render(Join().Single(roll => roll.RollCall.Part == 1));

        Assert.Equal("""
            Senate vote: 119 1 1
            Started: 2025-01-09
            Question: On the Cloture Motion
            Vote title: A bill to require the Secretary of Homeland Security to take into custody aliens who have been charged in the United States with theft, and for other purposes.
            Result: Cloture Motion Agreed to
            Majority required: 3/5
            Count: Yea 4, Nay 2, Present 0, Not voting 0
            Member: C001088, COONS, Christopher A., D, DE, Yea
            Member: G000386, GRASSLEY, Charles Ernest, R, IA, Yea
            Member: K000383, KING, Angus Stanley, Jr., I, ME, Yea
            Member: M000133, MARKEY, Edward John, D, MA, Nay
            Member: S000033, SANDERS, Bernard, I, VT, Nay
            Member: T000278, TUBERVILLE, Thomas Hawley (Tommy), R, AL, Yea
            """, text);
    }

    [Fact]
    public void ThePresidentRowIsLeftOutBecauseHeIsNotASenatorAndWouldBreakTheYeaCount()
    {
        var roll = Join().Single(item => item.RollCall.Part == 1);

        Assert.DoesNotContain(roll.Votes, vote => vote.Member.Icpsr == 99912);
        Assert.Equal(4, roll.Count(VoteviewCast.Yea));
    }

    [Fact]
    public void ARollCallWhoseMemberCountsDisagreeWithTheFileCountsIsSkippedAndTheOtherRollCallsStillRender()
    {
        var parts = Join().Select(roll => roll.RollCall.Part);

        Assert.Equal([1, 60], parts);
    }

    [Fact]
    public void ASenateMemberWithNoBioguideIdIsRefusedBecauseTheRecordCouldNotName()
    {
        var members = VoteviewVoteFixtures.DefaultMembers.Replace("T000278", "");

        Assert.Throws<InvalidDataException>(() => Join(members: members));
    }

    [Fact]
    public void AVoteByAnIcpsrThatTheMemberFileDoesNotListIsRefusedInsteadOfDropped()
    {
        var votes = VoteviewVoteFixtures.DefaultVotes + "119,Senate,60,11111,1,100.0\n";

        Assert.Throws<InvalidDataException>(() => Join(votes: votes));
    }

    [Fact]
    public void ASenatorWhoVotesTwiceInOneRollCallIsRefused()
    {
        var votes = VoteviewVoteFixtures.DefaultVotes + "119,Senate,60,42102,6,100.0\n";

        Assert.Throws<InvalidDataException>(() => Join(votes: votes));
    }

    [Theory]
    [InlineData("119,Senate,60,42102,3,100.0\n")]
    [InlineData("119,Senate,60,42102,abc,100.0\n")]
    public void ACastCodeOutsideYeaNayPresentAndNotVotingIsRefusedBecauseItsMeaningIsUnknown(string row)
    {
        var votes = VoteviewVoteFixtures.VotesHeader + "\n" + row;

        Assert.Throws<InvalidDataException>(() => VoteviewParser.ParseCasts(VoteviewVoteFixtures.Bytes(votes), 119));
    }

    [Fact]
    public void APartyCodeOutsideDemocratRepublicanAndIndependentIsRefusedBecauseTheLetterWouldBeAGuess()
    {
        var members = VoteviewVoteFixtures.DefaultMembers.Replace("AL,200", "AL,555");

        Assert.Throws<InvalidDataException>(() => Join(members: members));
    }

    [Fact]
    public void AFileOfAnotherCongressIsRefusedSoAJobUrlCannotFeedTheWrongCongress()
    {
        Assert.Throws<InvalidDataException>(() =>
            VoteviewParser.ParseRollCalls(VoteviewVoteFixtures.Bytes(VoteviewVoteFixtures.DefaultRollCalls), 118));
    }

    [Fact]
    public void ANominationRollCallHasNoBillButABillRollCallNamesItsTypeAndNumber()
    {
        var rollCalls = VoteviewParser.ParseRollCalls(VoteviewVoteFixtures.Bytes(VoteviewVoteFixtures.DefaultRollCalls), 119);

        Assert.Equal([("s", 5), (null, null)],
            rollCalls.Where(rollCall => rollCall.Part != 2).Select(rollCall => (rollCall.BillType, rollCall.BillNumber)));
    }

    [Fact]
    public void AQuotedFieldMayHoldCommasQuotesAndLineBreaksAndStillRendersOnOneLine()
    {
        var rollCalls = VoteviewVoteFixtures.DefaultRollCalls.Replace("On the Cloture Motion,\n", "\"On the \"\"Cloture\"\"\r\nMotion\",\n");

        var roll = Join(rollCalls).Single(item => item.RollCall.Part == 1);

        Assert.Contains("Question: On the \"Cloture\" Motion\n", VoteviewRenderer.Render(roll));
    }
}
