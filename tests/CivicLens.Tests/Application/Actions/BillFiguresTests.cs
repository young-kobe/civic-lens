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
    [InlineData("It pays $1,000,000,000,000 over ten years.", 1)]
    [InlineData("It pays $1 trillion.", 1)]
    [InlineData("It has 2 billion people and 50 percent.", 0)]
    public void ADollarAmountIsADollarSignWithDigitsOrDigitsBeforeDollars(string text, int amounts) =>
        Assert.Equal(amounts, BillFigures.Count(text).Amounts);

    [Theory]
    [InlineData("It starts on January 1, 2027.", 1)]
    [InlineData("It starts in January 2027.", 1)]
    [InlineData("It covers fiscal year 2026.", 1)]
    [InlineData("It covers fiscal years 2026 through 2030.", 1)]
    [InlineData("It runs from March 5, 2026 to June 30, 2031.", 2)]
    [InlineData("It starts by 1999 and ends before 2099.", 2)]
    [InlineData("It starts not later than 2027.", 1)]
    [InlineData("Not later than January 1, 2027, the Secretary acts.", 1)]
    [InlineData("It starts after 2027 and runs beginning 2028 or ending 2030.", 3)]
    [InlineData("It amends the Internal Revenue Code of 1986.", 0)]
    [InlineData("It amends the Social Security Act of 1935.", 0)]
    [InlineData("It amends 42 U.S.C. 1981.", 0)]
    [InlineData("It amends section 2001.", 0)]
    [InlineData("Under the Act of 1935 the Secretary acts by 2031.", 1)]
    [InlineData("It starts in 1999 and ends in 2099.", 0)]
    [InlineData("It starts in 2100 and the section is 1850X.", 0)]
    [InlineData("It starts soon.", 0)]
    public void ADateIsAYearAfterAMonthFiscalYearOrDeadlinePhraseAndNeverAYearOfAnActOrCodeOrANumberAfterSectionOrUsc(string text, int dates) =>
        Assert.Equal(dates, BillFigures.Count(text).Dates);

    [Fact]
    public void ADateWithAMonthAndDayCountsOnceAndAnAmountIsNotADate() =>
        Assert.Equal(new BillFigureCounts(1, 1), BillFigures.Count("Pay $2,000 by December 31, 2030."));

    [Fact]
    public void MissingDatesListsTheDatesOfTheTextWithoutTheirDeadlinePhraseThatNoSentenceWritesAndTheCountUsesTheSameMatches()
    {
        const string text = "Not later than September 30, 2027, the Secretary acts. It covers fiscal year 2026 and fiscal year 2026. It starts by 2028.";

        Assert.Equal(["September 30, 2027", "fiscal year 2026", "2028"], BillFigures.MissingDates(text, "The bill is plain."));
        Assert.Equal(["fiscal year 2026"], BillFigures.MissingDates(text, "It acts by September 30, 2027 and starts after 2028."));
        Assert.Equal(["September 30, 2027"], BillFigures.MissingDates(text, "It covers Fiscal Year 2026 and starts before 2028."));
        Assert.Empty(BillFigures.MissingDates(text, "By September 30, 2027, fiscal year 2026 and after 2028."));
        Assert.Equal(4, BillFigures.Count(text).Dates);
    }
}
