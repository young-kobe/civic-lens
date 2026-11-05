using CivicLens.Application.Votes;

namespace CivicLens.Tests.Application.Votes;

public sealed class VoteResultTableTests
{
    [Theory]
    [InlineData("Passed", VoteOutcome.Passed)]
    [InlineData("Failed", VoteOutcome.Failed)]
    [InlineData("Agreed to", VoteOutcome.AgreedTo)]
    [InlineData("Confirmed", VoteOutcome.Confirmed)]
    [InlineData("Rejected", VoteOutcome.Rejected)]
    [InlineData("Cloture Motion Agreed to", VoteOutcome.AgreedTo)]
    [InlineData("Motion to Proceed Agreed to", VoteOutcome.AgreedTo)]
    [InlineData("Nomination Confirmed", VoteOutcome.Confirmed)]
    [InlineData("Joint Resolution Passed", VoteOutcome.Passed)]
    [InlineData("  passed ", VoteOutcome.Passed)]
    public void ResultWordsThatEndWithAKnownOutcomeMapToItSoThePageCanSayItInPlainWords(string result, VoteOutcome expected) =>
        Assert.Equal(expected, VoteResultTable.OutcomeOf(result));

    [Theory]
    [InlineData("Point of Order Sustained")]
    [InlineData("Not Agreed to")]
    [InlineData("Nomination Not Confirmed")]
    [InlineData("Passed <b>")]
    [InlineData("Unpassed")]
    [InlineData("")]
    public void ResultWordsThatNoRowKnowsHaveNoOutcomeSoThePageShowsTheSourceWordsAlone(string result) =>
        Assert.Null(VoteResultTable.OutcomeOf(result));
}
