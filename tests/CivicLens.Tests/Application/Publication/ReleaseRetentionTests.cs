using CivicLens.Application.Publication;

namespace CivicLens.Tests.Application.Publication;

public sealed class ReleaseRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 2, 10, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ABurstOfFiveHundredReleasesInOneDayKeepsThirtyPlusOlderDailyReleasesSoTheDiskStaysBounded()
    {
        var older = Enumerable.Range(1, 6).Select(day => (Number: day, At: Now.AddDays(-(7 - day)).AddHours(-3)));
        var burst = Enumerable.Range(7, 500).Select(number => (Number: number, At: Now.AddHours(-1)));

        var plan = ReleaseRetention.Plan([.. older, .. burst], Now);

        var expected = Enumerable.Range(477, 30).Concat(Enumerable.Range(1, 6)).Order().ToArray();
        Assert.Equal(expected, plan.KeptNumbers.Order().ToArray());
    }

    [Fact]
    public void ADayWithNoReleaseKeepsNothingExtraSoOnlyTheNewestThirtyRemain()
    {
        var stamps = Enumerable.Range(1, 40).Select(number => (number, Now.AddDays(-2)));

        var plan = ReleaseRetention.Plan(stamps, Now);

        Assert.Equal(Enumerable.Range(11, 30).ToArray(), plan.KeptNumbers.Order().ToArray());
    }

    [Fact]
    public void OnlySixPastDaysAndTodayAreKeptSoADayEightDaysBackKeepsNothing()
    {
        var daily = Enumerable.Range(1, 8).Select(number => (number, Now.AddDays(number - 8)));
        var burst = Enumerable.Range(9, 40).Select(number => (number, Now));

        var plan = ReleaseRetention.Plan([.. daily, .. burst], Now);

        Assert.Equal(Enumerable.Range(2, 6).Concat(Enumerable.Range(19, 30)).ToArray(), plan.KeptNumbers.Order().ToArray());
    }

    [Fact]
    public void TheUtcMidnightSplitsTwoDaysEvenWhenTheReleasesAreMinutesApart()
    {
        var beforeMidnight = new DateTimeOffset(2026, 2, 5, 23, 59, 0, TimeSpan.Zero);
        var afterMidnight = new DateTimeOffset(2026, 2, 6, 0, 1, 0, TimeSpan.Zero);
        var filler = Enumerable.Range(10, 30).Select(number => (number, Now));

        var plan = ReleaseRetention.Plan([(1, beforeMidnight), (2, beforeMidnight.AddSeconds(30)), (3, afterMidnight), .. filler], Now);

        Assert.Contains(2, plan.KeptNumbers);
        Assert.Contains(3, plan.KeptNumbers);
        Assert.DoesNotContain(1, plan.KeptNumbers);
    }

    [Fact]
    public void AnOffsetTimestampCountsOnItsUtcDayNotItsLocalDay()
    {
        var localEvening = new DateTimeOffset(2026, 2, 3, 22, 0, 0, TimeSpan.FromHours(-5));
        var filler = Enumerable.Range(10, 30).Select(number => (number, Now));

        var plan = ReleaseRetention.Plan([(1, localEvening), (2, new DateTimeOffset(2026, 2, 4, 2, 0, 0, TimeSpan.Zero)), .. filler], Now);

        Assert.Contains(2, plan.KeptNumbers);
        Assert.DoesNotContain(1, plan.KeptNumbers);
    }

    [Fact]
    public void TheDailyWindowStartsAtUtcMidnightSixDaysBackSoSevenDaysCountTheCurrentOne()
    {
        Assert.Equal(new DateTimeOffset(2026, 2, 4, 0, 0, 0, TimeSpan.Zero), ReleaseRetention.DailyWindowStart(Now));
    }

    [Fact]
    public void ARerunOnTheSameInputKeepsTheSameSetSoAPruneChangesNothingTheSecondTime()
    {
        var stamps = Enumerable.Range(1, 80).Select(number => (number, Now.AddHours(-(80 - number) * 3))).ToList();

        var first = ReleaseRetention.Plan(stamps, Now);
        var survivors = stamps.Where(stamp => !first.Retires(stamp.number)).ToList();
        var second = ReleaseRetention.Plan(survivors, Now);

        Assert.Equal(first.KeptNumbers.Order(), second.KeptNumbers.Order());
        Assert.All(survivors, stamp => Assert.False(second.Retires(stamp.number)));
    }

    [Fact]
    public void ANumberAboveTheHighestCommittedReleaseIsNeverRetiredBecauseAPublishInProgressMayOwnIt()
    {
        var plan = ReleaseRetention.Plan(Enumerable.Range(1, 40).Select(number => (number, Now)), Now);

        Assert.True(plan.Retires(10));
        Assert.False(plan.Retires(40));
        Assert.False(plan.Retires(41));
    }
}
