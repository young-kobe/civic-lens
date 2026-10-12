using CivicLens.Core.Statements;

namespace CivicLens.Tests.Core.Statements;

public sealed class BillNumberFinderTests
{
    [Theory]
    [InlineData("the bill H.R. 1234 passed", "H.R. 1234")]
    [InlineData("the bill H.R.1234 passed", "H.R. 1234")]
    [InlineData("the bill H. R. 1234 passed", "H.R. 1234")]
    [InlineData("Senate bill S. 56.", "S. 56")]
    [InlineData("see S.1234", "S. 1234")]
    [InlineData("H.Res. 9 honors", "H.Res. 9")]
    [InlineData("S.Res. 301 honors", "S.Res. 301")]
    [InlineData("H.J.Res.7 would", "H.J.Res. 7")]
    [InlineData("S.J.Res. 12 would", "S.J.Res. 12")]
    [InlineData("H.Con.Res. 3 sets", "H.Con.Res. 3")]
    [InlineData("S.Con.Res. 4 sets", "S.Con.Res. 4")]
    [InlineData("(H.R. 99999)", "H.R. 99999")]
    public void EachCongressBillTypeIsWrittenInOneForm(string text, string expected) =>
        Assert.Equal([expected], BillNumberFinder.Find(text).ToArray());

    [Theory]
    [InlineData("The U.S. 2026 budget")]
    [InlineData("Section 5 of the act")]
    [InlineData("HR 1234 is a department")]
    [InlineData("S. 0123")]
    [InlineData("H.R. 123456")]
    [InlineData("CLASS. 5 students")]
    [InlineData("H.R. 12A")]
    public void TextThatOnlyLooksLikeABillGivesNoNumber(string text) => Assert.Empty(BillNumberFinder.Find(text));

    [Fact]
    public void RepeatedNumbersAppearOnceInOrderOfFirstUse() =>
        Assert.Equal(["H.R. 56", "S. 1234", "S.J.Res. 2"],
            BillNumberFinder.Find("H.R. 56 and S. 1234, then H.R.56 and S.J.Res. 2 and S. 1234 again.").ToArray());
}
