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
        Assert.Equal("The nominee is confirmed and can now take the office.", Of(PublishedVoteKind.Nomination, "Confirmed", PublishedChamber.Senate));
        Assert.StartsWith("The nominee is not confirmed", Of(PublishedVoteKind.Nomination, "Not Confirmed", PublishedChamber.Senate), StringComparison.Ordinal);
    }
}
