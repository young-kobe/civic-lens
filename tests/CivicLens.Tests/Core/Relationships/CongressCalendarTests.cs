using CivicLens.Core.Relationships;

namespace CivicLens.Tests.Core.Relationships;

public sealed class CongressCalendarTests
{
    [Theory]
    [InlineData(2025, 1, 3, 119)]
    [InlineData(2025, 1, 2, 118)]
    [InlineData(2025, 1, 1, 118)]
    [InlineData(2026, 12, 31, 119)]
    [InlineData(2027, 1, 2, 119)]
    [InlineData(2027, 1, 3, 120)]
    [InlineData(2024, 6, 15, 118)]
    [InlineData(1935, 1, 3, 74)]
    public void ACongressStartsOnThirdJanuaryOfAnOddYearAndTheFirstTwoDaysBelongToTheCongressBefore(int year, int month, int day, int congress) =>
        Assert.Equal(congress, CongressCalendar.CongressOn(new DateOnly(year, month, day)));

    [Fact]
    public void ADateBeforeThe20thAmendmentTermStartHasNoCongressBecauseTheOldTermStartedInMarch() =>
        Assert.Null(CongressCalendar.CongressOn(new DateOnly(1935, 1, 2)));

    [Theory]
    [InlineData(119, 2025)]
    [InlineData(120, 2027)]
    public void ACongressStartsOnThirdJanuaryAndTheDayBeforeBelongsToThePreviousCongress(int congress, int year)
    {
        var start = CongressCalendar.StartOf(congress);

        Assert.Equal(new DateOnly(year, 1, 3), start);
        Assert.Equal((congress, congress - 1), (CongressCalendar.CongressOn(start), CongressCalendar.CongressOn(start.AddDays(-1))));
    }
}
