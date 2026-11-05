using CivicLens.Application.Actions;

namespace CivicLens.Tests.Application.Actions;

public sealed class BillFiguresTests
{
    [Theory]
    [InlineData("The grant is $2,000,000.", 1)]
    [InlineData("The grant is $2 billion.", 1)]
    [InlineData("It pays $1,500.50 and then $300.", 2)]
    [InlineData("It pays 5 million dollars.", 1)]
    [InlineData("It pays 400 dollars.", 1)]
    [InlineData("It pays a fee.", 0)]
    [InlineData("It has 2 billion people and 50 percent.", 0)]
    public void ADollarAmountIsADollarSignWithDigitsOrDigitsBeforeDollars(string text, int amounts) =>
        Assert.Equal(amounts, BillFigures.Count(text).Amounts);

    [Theory]
    [InlineData("It starts on January 1, 2027.", 1)]
    [InlineData("It starts in January 2027.", 1)]
    [InlineData("It covers fiscal year 2026.", 1)]
    [InlineData("It runs from March 5, 2026 to June 30, 2031.", 2)]
    [InlineData("It starts in 1999 and ends in 2099.", 2)]
    [InlineData("It starts in 2100 and the section is 1850X.", 0)]
    [InlineData("It starts soon.", 0)]
    public void ADateIsAYearWithAnOptionalMonthAndDay(string text, int dates) =>
        Assert.Equal(dates, BillFigures.Count(text).Dates);

    [Fact]
    public void ADateWithAMonthAndDayCountsOnceAndAnAmountIsNotADate() =>
        Assert.Equal(new BillFigureCounts(1, 1), BillFigures.Count("Pay $2,000 by December 31, 2030."));
}
