using CivicLens.Application.Actions;

namespace CivicLens.Tests.Application.Actions;

public sealed class BillFigureSetTests
{
    private const string Text = "The grant is $50,000,000 for fiscal year 2027. A fee of $1,500.50 or 2 billion dollars or $1.5 billion applies in 1999.";

    [Theory]
    [InlineData("The bill gives $50 million.")]
    [InlineData("The bill gives $50,000,000.")]
    [InlineData("The bill gives 50 million dollars.")]
    [InlineData("The fee is $1,500.50.")]
    [InlineData("It spends $2 billion.")]
    [InlineData("It spends $2,000,000,000.")]
    [InlineData("It spends $1.5 billion.")]
    [InlineData("It spends $1,500 million.")]
    [InlineData("The grant starts in 2027 and the fee in 1999.")]
    [InlineData("It pays a fee.")]
    public void AFigureThatTheTextHasPassesWhateverWayItIsWritten(string claim) => Assert.Empty(new BillFigureSet(Text).Missing(claim, "1"));

    [Theory]
    [InlineData("The bill gives $5 million.", "$5 million")]
    [InlineData("The bill gives $500,000,000.", "$500,000,000")]
    [InlineData("The bill gives 5 thousand dollars.", "5 thousand dollars")]
    [InlineData("The grant starts in 2028.", "2028")]
    public void AFigureThatTheTextLacksIsNamed(string claim, string figure) => Assert.Equal([figure], new BillFigureSet(Text).Missing(claim, "1"));

    [Fact]
    public void AFullAmountOfManyDigitsKeepsEveryDigitSoATrillionIsNotAHundredBillion()
    {
        var figures = new BillFigureSet("The fund holds $1,000,000,000,000 in all.");

        Assert.Empty(figures.Missing("The fund holds $1 trillion.", "1"));
        Assert.Equal(["$100 billion"], figures.Missing("The fund holds $100 billion.", "1"));
    }

    [Theory]
    [InlineData("H.R. 2025 ends the program.")]
    [InlineData("S. 2025 ends the program.")]
    [InlineData("H.J. Res. 2025 ends the program.")]
    public void TheCitationOfTheBillIsNotAYear(string claim)
    {
        var figures = new BillFigureSet("The program ends on December 31, 2030.");

        Assert.Empty(figures.Missing(claim, "2025"));
        Assert.Equal(["2025"], figures.Missing(claim, "1"));
    }

    [Fact]
    public void AYearEqualToTheBillNumberStillNeedsTheTextWhenItIsNotTheCitation()
    {
        var figures = new BillFigureSet("The program ends on December 31, 2030.");

        Assert.Equal(["2027"], figures.Missing("H.R. 2027 starts in 2027.", "2027"));
    }

    [Fact]
    public void ATextWithNoBillNumberSkipsNoCitation() =>
        Assert.Equal(["2025"], new BillFigureSet("The program ends on December 31, 2030.").Missing("H.R. 2025 ends the program.", null));
}
