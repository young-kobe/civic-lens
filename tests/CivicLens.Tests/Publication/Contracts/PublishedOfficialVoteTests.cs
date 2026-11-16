using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PublishedOfficialVoteTests
{
    [Theory]
    [InlineData("Yea", "Yea")]
    [InlineData("Aye", "Yea")]
    [InlineData("Nay", "Nay")]
    [InlineData("No", "Nay")]
    [InlineData("Present", null)]
    [InlineData("Not Voting", null)]
    [InlineData("Pelosi", null)]
    public void AyeAndNoAreTheYeaAndNaySidesAndEveryOtherCastHasNoSide(string cast, string? side)
    {
        Assert.Equal(side, PublishedOfficialVote.SideOf(cast));
    }
}
