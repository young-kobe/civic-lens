using CivicLens.Application.Collection.HouseVotes;

namespace CivicLens.Tests.Application.Collection.HouseVotes;

public sealed class HouseVoteSessionTests
{
    [Theory]
    [InlineData(2025, 119, 1)]
    [InlineData(2026, 119, 2)]
    [InlineData(2027, 120, 1)]
    [InlineData(2024, 118, 2)]
    public void ASessionIsTheCalendarYearBecauseTheApiNumbersVotesBySessionOfThePair(int year, int congress, int number)
    {
        var session = HouseVoteSession.ForYear(year);

        Assert.Equal((congress, number), (session.Congress, session.Number));
        Assert.Equal(year, session.Year);
    }

    [Fact]
    public void TheSweepStartsAtTheSessionOfTheBackfillDate()
    {
        Assert.Equal(new HouseVoteSession(119, 1),
            HouseVoteSession.FirstOfSweep(new DateOnly(2025, 1, 3), new DateOnly(2026, 10, 16), 119));
    }

    [Fact]
    public void TheSweepStartsAtTheMinimumCongressWhenTheBackfillDateIsEarlier()
    {
        Assert.Equal(new HouseVoteSession(119, 1),
            HouseVoteSession.FirstOfSweep(new DateOnly(2023, 1, 3), new DateOnly(2026, 10, 16), 119));
    }

    [Fact]
    public void NoSweepExistsBeforeTheSessionOfTheBackfillDateBegins()
    {
        Assert.Null(HouseVoteSession.FirstOfSweep(new DateOnly(2027, 1, 3), new DateOnly(2026, 12, 31), 119));
    }

    [Fact]
    public void TheSweepIncludesTheSessionOfTheBackfillYearOnTheFirstDay()
    {
        Assert.Equal(new HouseVoteSession(119, 1),
            HouseVoteSession.FirstOfSweep(new DateOnly(2025, 1, 3), new DateOnly(2025, 1, 3), 119));
    }

    [Theory]
    [InlineData(2026, 12, 31, 119, 1, 119, 2)]
    [InlineData(2026, 12, 31, 119, 2, null, null)]
    [InlineData(2027, 1, 1, 119, 2, 120, 1)]
    [InlineData(2027, 1, 1, 120, 1, null, null)]
    [InlineData(2025, 6, 1, 119, 1, null, null)]
    public void TheSweepFollowsSessionByStepUntilTheSessionOfToday(int year, int month, int day, int congress, int number,
        int? nextCongress, int? nextNumber)
    {
        var following = new HouseVoteSession(congress, number).Following(new DateOnly(year, month, day));

        Assert.Equal(nextCongress is null ? null : new HouseVoteSession(nextCongress.Value, nextNumber!.Value), following);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(119, 0)]
    [InlineData(119, 3)]
    public void ASessionNeedsACongressAndSessionNumberOneOrTwo(int congress, int number)
    {
        Assert.Null(HouseVoteSession.TryCreate(congress, number));
    }
}
