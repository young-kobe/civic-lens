using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class NextStepTests
{
    private static string? Of(PublishedVoteKind kind, string result, PublishedChamber chamber, string? billType = null) =>
        NextStep.Of(kind, result, chamber, billType);

    [Fact]
    public void AHouseBillThatPassedUsuallyGoesToTheSenateAndTheTextSaysTheCaseIsNotCertain()
    {
        var text = Of(PublishedVoteKind.Passage, "Passed", PublishedChamber.House, "hr");

        Assert.Contains("It usually goes to the Senate next.", text, StringComparison.Ordinal);
        Assert.Contains("does not say which case applies", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ASenateBillThatPassedTheHouseGoesToThePresidentOrBackToTheHouseByWhetherTheTextChanged()
    {
        var text = Of(PublishedVoteKind.Passage, "Passed", PublishedChamber.House, "s");

        Assert.Contains("Both chambers have now passed it.", text, StringComparison.Ordinal);
        Assert.Contains("did not change the text, it goes to the President", text, StringComparison.Ordinal);
        Assert.Contains("goes back to the Senate", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AConcurrentResolutionNeverGoesToThePresidentAndASimpleResolutionGoesNoFurther()
    {
        var concurrent = Of(PublishedVoteKind.Passage, "Agreed to", PublishedChamber.Senate, "hconres");
        var simple = Of(PublishedVoteKind.Passage, "Agreed to", PublishedChamber.Senate, "sres");

        Assert.Contains("does not go to the President", concurrent, StringComparison.Ordinal);
        Assert.Contains("goes no further", simple, StringComparison.Ordinal);
        Assert.DoesNotContain("President", simple, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailedPassageGoesNoFurtherUnlessTheChamberVotesAgain() =>
        Assert.Equal("The Senate did not pass it. It goes no further unless the Senate votes on it again.",
            Of(PublishedVoteKind.Passage, "Failed", PublishedChamber.Senate, "s"));

    [Theory]
    [InlineData(PublishedVoteKind.Rule, PublishedChamber.Senate)]
    [InlineData(PublishedVoteKind.Suspension, PublishedChamber.Senate)]
    [InlineData(PublishedVoteKind.Nomination, PublishedChamber.House)]
    [InlineData(PublishedVoteKind.Cloture, PublishedChamber.House)]
    [InlineData(PublishedVoteKind.Other, PublishedChamber.House)]
    public void AKindThatThisChamberDoesNotHoldOrThatIsOtherGivesNoStepBecauseAGuessWouldMisleadTheReader(PublishedVoteKind kind, PublishedChamber chamber) =>
        Assert.Null(Of(kind, "Passed", chamber, "hr"));

    [Theory]
    [InlineData("Guilty")]
    [InlineData("Tie")]
    [InlineData("")]
    public void AnUnknownResultGivesNoStep(string result) =>
        Assert.Null(Of(PublishedVoteKind.Passage, result, PublishedChamber.House, "hr"));

    [Theory]
    [InlineData(null)]
    [InlineData("xyz")]
    public void APassedVoteOnABillWithNoKnownTypeGivesNoStepBecauseTheRouteDependsOnTheType(string? billType) =>
        Assert.Null(Of(PublishedVoteKind.Passage, "Passed", PublishedChamber.House, billType));

    [Fact]
    public void ANominationGivesTheConfirmedOrNotConfirmedStep()
    {
        Assert.Equal("The nominee is confirmed. The President can now appoint the nominee to the office.", Of(PublishedVoteKind.Nomination, "Confirmed", PublishedChamber.Senate));
        Assert.StartsWith("The nominee is not confirmed", Of(PublishedVoteKind.Nomination, "Not Confirmed", PublishedChamber.Senate), StringComparison.Ordinal);
    }

    public static TheoryData<PublishedVoteKind, string, PublishedChamber, string?, string> Steps => new()
    {
        { PublishedVoteKind.Rule, "Passed", PublishedChamber.House, null, "The House can now debate the bill under these rules. A vote on the bill usually follows." },
        { PublishedVoteKind.Rule, "Failed", PublishedChamber.House, null, "The bill cannot move to debate under this rule. House leaders may try again with a new rule." },
        { PublishedVoteKind.PreviousQuestion, "Passed", PublishedChamber.House, null, "Debate is over. The House now votes on the matter that was being debated." },
        { PublishedVoteKind.PreviousQuestion, "Failed", PublishedChamber.House, null, "Debate is not over. The minority can now offer a change to the matter being debated." },
        { PublishedVoteKind.MotionToRecommit, "Passed", PublishedChamber.House, null, "The bill goes back to a committee. It usually goes no further." },
        { PublishedVoteKind.MotionToRecommit, "Failed", PublishedChamber.House, null, "The bill stays before the House. A vote on passing it usually follows." },
        { PublishedVoteKind.Amendment, "Passed", PublishedChamber.House, null, "The change is now part of the text. The House can still vote on other changes before it votes on the bill itself." },
        { PublishedVoteKind.Amendment, "Passed", PublishedChamber.Senate, null, "The change is now part of the text. The Senate can still vote on other changes before it votes on the bill itself." },
        { PublishedVoteKind.Amendment, "Failed", PublishedChamber.House, null, "The text stays as it was. The vote on the bill itself comes later." },
        { PublishedVoteKind.Amendment, "Failed", PublishedChamber.Senate, null, "The text stays as it was. The vote on the bill itself comes later." },
        { PublishedVoteKind.Suspension, "Passed", PublishedChamber.House, "hr", "The House passed it. It usually goes to the Senate next. If this vote agreed to a change from the Senate, it goes to the President instead. This record does not say which case applies." },
        { PublishedVoteKind.Suspension, "Passed", PublishedChamber.House, "s", "Both chambers have now passed it. If the House did not change the text, it goes to the President. If the House changed the text, it goes back to the Senate. This record does not say which case applies." },
        { PublishedVoteKind.Suspension, "Passed", PublishedChamber.House, "hjres", "The House passed it. It usually goes to the Senate next. If this vote agreed to a change from the Senate, it goes to the President instead. A joint resolution that proposes an amendment to the Constitution goes to the states, not the President. This record does not say which case applies." },
        { PublishedVoteKind.Suspension, "Passed", PublishedChamber.House, "sjres", "Both chambers have now passed it. If the House did not change the text, it goes to the President. If the House changed the text, it goes back to the Senate. A joint resolution that proposes an amendment to the Constitution goes to the states, not the President. This record does not say which case applies." },
        { PublishedVoteKind.Suspension, "Passed", PublishedChamber.House, "hconres", "The House passed it. It usually goes to the Senate next. If this vote agreed to a change from the Senate, Congress has now adopted it instead. A concurrent resolution does not go to the President. This record does not say which case applies." },
        { PublishedVoteKind.Suspension, "Passed", PublishedChamber.House, "sconres", "Both chambers have now passed it. If the House did not change the text, Congress has adopted it. If the House changed the text, it goes back to the Senate. It does not go to the President. This record does not say which case applies." },
        { PublishedVoteKind.Suspension, "Passed", PublishedChamber.House, "hres", "The House passed it. A simple resolution needs only one chamber, so it goes no further." },
        { PublishedVoteKind.Suspension, "Passed", PublishedChamber.House, "sres", "The House passed it. A simple resolution needs only one chamber, so it goes no further." },
        { PublishedVoteKind.Suspension, "Failed", PublishedChamber.House, null, "The House did not pass it by this fast route. It can come back to the floor under the normal rules." },
        { PublishedVoteKind.Passage, "Passed", PublishedChamber.House, "hr", "The House passed it. It usually goes to the Senate next. If this vote agreed to a change from the Senate, it goes to the President instead. This record does not say which case applies." },
        { PublishedVoteKind.Passage, "Passed", PublishedChamber.House, "s", "Both chambers have now passed it. If the House did not change the text, it goes to the President. If the House changed the text, it goes back to the Senate. This record does not say which case applies." },
        { PublishedVoteKind.Passage, "Passed", PublishedChamber.House, "hjres", "The House passed it. It usually goes to the Senate next. If this vote agreed to a change from the Senate, it goes to the President instead. A joint resolution that proposes an amendment to the Constitution goes to the states, not the President. This record does not say which case applies." },
        { PublishedVoteKind.Passage, "Passed", PublishedChamber.House, "sjres", "Both chambers have now passed it. If the House did not change the text, it goes to the President. If the House changed the text, it goes back to the Senate. A joint resolution that proposes an amendment to the Constitution goes to the states, not the President. This record does not say which case applies." },
        { PublishedVoteKind.Passage, "Passed", PublishedChamber.House, "hconres", "The House passed it. It usually goes to the Senate next. If this vote agreed to a change from the Senate, Congress has now adopted it instead. A concurrent resolution does not go to the President. This record does not say which case applies." },
        { PublishedVoteKind.Passage, "Passed", PublishedChamber.House, "sconres", "Both chambers have now passed it. If the House did not change the text, Congress has adopted it. If the House changed the text, it goes back to the Senate. It does not go to the President. This record does not say which case applies." },
        { PublishedVoteKind.Passage, "Passed", PublishedChamber.House, "hres", "The House passed it. A simple resolution needs only one chamber, so it goes no further." },
        { PublishedVoteKind.Passage, "Passed", PublishedChamber.House, "sres", "The House passed it. A simple resolution needs only one chamber, so it goes no further." },
        { PublishedVoteKind.Passage, "Passed", PublishedChamber.Senate, "hr", "Both chambers have now passed it. If the Senate did not change the text, it goes to the President. If the Senate changed the text, it goes back to the House. This record does not say which case applies." },
        { PublishedVoteKind.Passage, "Passed", PublishedChamber.Senate, "s", "The Senate passed it. It usually goes to the House next. If this vote agreed to a change from the House, it goes to the President instead. This record does not say which case applies." },
        { PublishedVoteKind.Passage, "Passed", PublishedChamber.Senate, "hjres", "Both chambers have now passed it. If the Senate did not change the text, it goes to the President. If the Senate changed the text, it goes back to the House. A joint resolution that proposes an amendment to the Constitution goes to the states, not the President. This record does not say which case applies." },
        { PublishedVoteKind.Passage, "Passed", PublishedChamber.Senate, "sjres", "The Senate passed it. It usually goes to the House next. If this vote agreed to a change from the House, it goes to the President instead. A joint resolution that proposes an amendment to the Constitution goes to the states, not the President. This record does not say which case applies." },
        { PublishedVoteKind.Passage, "Passed", PublishedChamber.Senate, "hconres", "Both chambers have now passed it. If the Senate did not change the text, Congress has adopted it. If the Senate changed the text, it goes back to the House. It does not go to the President. This record does not say which case applies." },
        { PublishedVoteKind.Passage, "Passed", PublishedChamber.Senate, "sconres", "The Senate passed it. It usually goes to the House next. If this vote agreed to a change from the House, Congress has now adopted it instead. A concurrent resolution does not go to the President. This record does not say which case applies." },
        { PublishedVoteKind.Passage, "Passed", PublishedChamber.Senate, "hres", "The Senate passed it. A simple resolution needs only one chamber, so it goes no further." },
        { PublishedVoteKind.Passage, "Passed", PublishedChamber.Senate, "sres", "The Senate passed it. A simple resolution needs only one chamber, so it goes no further." },
        { PublishedVoteKind.Passage, "Failed", PublishedChamber.House, null, "The House did not pass it. It goes no further unless the House votes on it again." },
        { PublishedVoteKind.Passage, "Failed", PublishedChamber.Senate, null, "The Senate did not pass it. It goes no further unless the Senate votes on it again." },
        { PublishedVoteKind.ConferenceReport, "Passed", PublishedChamber.House, "hr", "The House accepted the final text. If the Senate has also accepted it, the bill goes to the President. If not, the Senate votes on it next." },
        { PublishedVoteKind.ConferenceReport, "Passed", PublishedChamber.House, "s", "The House accepted the final text. If the Senate has also accepted it, the bill goes to the President. If not, the Senate votes on it next." },
        { PublishedVoteKind.ConferenceReport, "Passed", PublishedChamber.House, "hjres", "The House accepted the final text. If the Senate has also accepted it, the bill goes to the President. If not, the Senate votes on it next. A joint resolution that proposes an amendment to the Constitution goes to the states, not the President." },
        { PublishedVoteKind.ConferenceReport, "Passed", PublishedChamber.House, "sjres", "The House accepted the final text. If the Senate has also accepted it, the bill goes to the President. If not, the Senate votes on it next. A joint resolution that proposes an amendment to the Constitution goes to the states, not the President." },
        { PublishedVoteKind.ConferenceReport, "Passed", PublishedChamber.House, "hconres", "The House accepted the final text. If the Senate has also accepted it, Congress has adopted it. If not, the Senate votes on it next. A concurrent resolution does not go to the President." },
        { PublishedVoteKind.ConferenceReport, "Passed", PublishedChamber.House, "sconres", "The House accepted the final text. If the Senate has also accepted it, Congress has adopted it. If not, the Senate votes on it next. A concurrent resolution does not go to the President." },
        { PublishedVoteKind.ConferenceReport, "Passed", PublishedChamber.Senate, "hr", "The Senate accepted the final text. If the House has also accepted it, the bill goes to the President. If not, the House votes on it next." },
        { PublishedVoteKind.ConferenceReport, "Passed", PublishedChamber.Senate, "s", "The Senate accepted the final text. If the House has also accepted it, the bill goes to the President. If not, the House votes on it next." },
        { PublishedVoteKind.ConferenceReport, "Passed", PublishedChamber.Senate, "hjres", "The Senate accepted the final text. If the House has also accepted it, the bill goes to the President. If not, the House votes on it next. A joint resolution that proposes an amendment to the Constitution goes to the states, not the President." },
        { PublishedVoteKind.ConferenceReport, "Passed", PublishedChamber.Senate, "sjres", "The Senate accepted the final text. If the House has also accepted it, the bill goes to the President. If not, the House votes on it next. A joint resolution that proposes an amendment to the Constitution goes to the states, not the President." },
        { PublishedVoteKind.ConferenceReport, "Passed", PublishedChamber.Senate, "hconres", "The Senate accepted the final text. If the House has also accepted it, Congress has adopted it. If not, the House votes on it next. A concurrent resolution does not go to the President." },
        { PublishedVoteKind.ConferenceReport, "Passed", PublishedChamber.Senate, "sconres", "The Senate accepted the final text. If the House has also accepted it, Congress has adopted it. If not, the House votes on it next. A concurrent resolution does not go to the President." },
        { PublishedVoteKind.ConferenceReport, "Failed", PublishedChamber.House, null, "The House did not accept the final text. It goes no further until both chambers agree on new text." },
        { PublishedVoteKind.ConferenceReport, "Failed", PublishedChamber.Senate, null, "The Senate did not accept the final text. It goes no further until both chambers agree on new text." },
        { PublishedVoteKind.VetoOverride, "Passed", PublishedChamber.House, null, "The House voted to override the veto. If the Senate also votes by two thirds to override it, the bill becomes law. If it already did, the bill is now law. This record does not say which case applies." },
        { PublishedVoteKind.VetoOverride, "Passed", PublishedChamber.Senate, null, "The Senate voted to override the veto. If the House also votes by two thirds to override it, the bill becomes law. If it already did, the bill is now law. This record does not say which case applies." },
        { PublishedVoteKind.VetoOverride, "Failed", PublishedChamber.House, null, "The veto stands. The bill does not become law." },
        { PublishedVoteKind.VetoOverride, "Failed", PublishedChamber.Senate, null, "The veto stands. The bill does not become law." },
        { PublishedVoteKind.Nomination, "Passed", PublishedChamber.Senate, null, "The nominee is confirmed. The President can now appoint the nominee to the office." },
        { PublishedVoteKind.Nomination, "Failed", PublishedChamber.Senate, null, "The nominee is not confirmed and does not take the office. The President may name someone else." },
        { PublishedVoteKind.Cloture, "Passed", PublishedChamber.Senate, null, "Debate is now limited, usually to 30 more hours. Then the Senate votes on the question before it. That can be a motion or the bill itself." },
        { PublishedVoteKind.Cloture, "Failed", PublishedChamber.Senate, null, "Debate can go on. Senate leaders may move on to other business." },
        { PublishedVoteKind.MotionToProceed, "Passed", PublishedChamber.Senate, null, "The Senate starts debate on the matter. A vote to end debate and a vote on the matter itself usually come later." },
        { PublishedVoteKind.MotionToProceed, "Failed", PublishedChamber.Senate, null, "The Senate does not start debate on the matter now. It may try again later." },
    };

    [Theory]
    [MemberData(nameof(Steps))]
    public void EachKindResultChamberAndBillShapeGivesItsExactSentenceSoASwapOfTwoSentencesFails(PublishedVoteKind kind, string result, PublishedChamber chamber, string? billType, string expected) =>
        Assert.Equal(expected, Of(kind, result, chamber, billType));

    [Fact]
    public void EveryOutputOfAnyInputIsOneOfThePinnedSentencesSoNoSentenceReachesTheReaderUnchecked()
    {
        var pinned = Steps.Select(row => (string)row[4]).ToHashSet();

        var unpinned = VoteWordingInputs.NextSteps().Select(step => step.Text).Distinct().Where(text => !pinned.Contains(text)).ToArray();

        Assert.True(unpinned.Length == 0, "Not pinned: " + string.Join(" | ", unpinned));
    }

    [Theory]
    [InlineData("Passed")]
    [InlineData("Agreed to")]
    [InlineData("Confirmed")]
    public void EveryPassingResultWordGivesTheSameStepAsPassed(string result) =>
        Assert.Equal(Of(PublishedVoteKind.Passage, "Passed", PublishedChamber.House, "hr"), Of(PublishedVoteKind.Passage, result, PublishedChamber.House, "hr"));

    [Theory]
    [InlineData("Failed")]
    [InlineData("Not Agreed to")]
    [InlineData("Not Confirmed")]
    public void EveryFailingResultWordGivesTheSameStepAsFailed(string result) =>
        Assert.Equal(Of(PublishedVoteKind.Passage, "Failed", PublishedChamber.House, "hr"), Of(PublishedVoteKind.Passage, result, PublishedChamber.House, "hr"));

    [Fact]
    public void AConferenceReportOnAnUnknownOrSimpleBillTypeGivesNoStepBecauseTheRouteDependsOnTheBillShape()
    {
        Assert.Null(Of(PublishedVoteKind.ConferenceReport, "Passed", PublishedChamber.House));
        Assert.Null(Of(PublishedVoteKind.ConferenceReport, "Passed", PublishedChamber.House, "hres"));
    }
}
