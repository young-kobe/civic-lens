using System.Security.Cryptography;
using System.Text;
using CivicLens.Core.Briefs;

namespace CivicLens.Tests.Core.Briefs;

public sealed class BriefWeekTests
{
    private static readonly DateOnly Monday = new(2026, 10, 5);

    [Fact]
    public void TheRecordIdIsTheSha256OfTheWeekSoEveryRunFindsTheSameBrief()
    {
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("brief|2026-10-05")));

        Assert.Equal(expected, BriefWeek.RecordIdOf(Monday));
        Assert.NotEqual(BriefWeek.RecordIdOf(Monday), BriefWeek.RecordIdOf(Monday.AddDays(7)));
    }

    [Theory]
    [InlineData(2026, 10, 4)]
    [InlineData(2026, 10, 6)]
    [InlineData(2026, 10, 11)]
    public void AWeekMustStartOnAMondayBecauseTheBriefPeriodIsMondayToMonday(int year, int month, int day)
    {
        var notMonday = new DateOnly(year, month, day);

        Assert.Throws<ArgumentException>(() => BriefWeek.RecordIdOf(notMonday));
        Assert.Throws<ArgumentException>(() => BriefWeek.RequireMonday(notMonday));
    }

    [Fact]
    public void ClosedWeeksRunFromTheFirstWeekUpToButNotIncludingTheCurrentWeek()
    {
        var weeks = BriefWeek.ClosedWeeks(Monday, Monday.AddDays(21)).ToArray();

        Assert.Equal([Monday, Monday.AddDays(7), Monday.AddDays(14)], weeks);
    }

    [Fact]
    public void TheWeekThatIsStillOpenIsNeverClosedSoNothingIsEnqueuedInTheFirstWeek()
    {
        Assert.Empty(BriefWeek.ClosedWeeks(Monday, Monday));
        Assert.Empty(BriefWeek.ClosedWeeks(Monday.AddDays(7), Monday));
    }
}
