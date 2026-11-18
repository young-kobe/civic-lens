using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class OfficialRollCallPageTests
{
    [Theory]
    [InlineData(118, 1, 7, "https://clerk.house.gov/Votes/20237")]
    [InlineData(118, 2, 7, "https://clerk.house.gov/Votes/20247")]
    public void TheHousePageUsesTheSessionYearAndTheUnpaddedRollCall(int congress, int session, int rollCall, string expected) =>
        Assert.Equal(expected, OfficialRollCallPage.Url(PublishedChamber.House, congress, session, rollCall));

    [Fact]
    public void TheSenatePageUsesCongressAndSessionInTheDirectoryAndFileWithAFiveDigitRollCall() =>
        Assert.Equal("https://www.senate.gov/legislative/LIS/roll_call_votes/vote1182/vote_118_2_00007.htm",
            OfficialRollCallPage.Url(PublishedChamber.Senate, 118, 2, 7));
}
