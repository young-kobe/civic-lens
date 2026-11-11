using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.Tests.PublicWeb;

public sealed class PartyBarTests
{
    [Theory]
    [InlineData(58, 152, 4, 0, 106)]
    [InlineData(3, 3, 0, 0, 4)]
    [InlineData(1, 0, 0, 0, 1)]
    public void TheMajorityLineIsTheSmallestYesCountAboveHalfOfTheYesAndNoCasts(int yes, int no, int notVoting, int other, int expected)
    {
        Assert.Equal(expected, new PartyBarData("Group", PartyTone.Neutral, yes, no, 0, notVoting, other).Majority);
    }

    [Fact]
    public void NobodyCastingYesOrNoGivesNoMajorityLine()
    {
        Assert.Null(new PartyBarData("Group", PartyTone.Neutral, 0, 0, 2, 3, 0).Majority);
    }

    [Fact]
    public void TheDescriptionNamesTheGroupAndEveryCount()
    {
        Assert.Equal("Group: Yes 1, No 2, Present 3, Not voting 4, Other 5", new PartyBarData("Group", PartyTone.Neutral, 1, 2, 3, 4, 5).Description);
    }
}
