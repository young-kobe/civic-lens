using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class BillNumbersTests
{
    [Theory]
    [InlineData("hr", 9576, "H.R. 9576")]
    [InlineData("HR", 9340, "H.R. 9340")]
    [InlineData("s", 2403, "S. 2403")]
    [InlineData("hjres", 213, "H.J.Res. 213")]
    [InlineData("SJRES", 4, "S.J.Res. 4")]
    [InlineData("hconres", 8, "H.Con.Res. 8")]
    [InlineData("sconres", 5, "S.Con.Res. 5")]
    [InlineData("hres", 61, "H.Res. 61")]
    [InlineData("sres", 3, "S.Res. 3")]
    public void ABillNumberPrintsInTheOfficialStyleWhateverTheCaseOfItsStoredType(string type, int number, string expected) =>
        Assert.Equal(expected, BillNumbers.Of(type, number));

    [Fact]
    public void ABillTypeOutsideTheTableShowsInUpperCaseInDisplayButFailsAsAStoredLabel()
    {
        Assert.Equal("HAMDT 7", BillNumbers.Of("hamdt", 7));
        Assert.Throws<KeyNotFoundException>(() => BillNumbers.TypeLabel("hamdt"));
    }

    [Theory]
    [InlineData("hr", "H.R.")]
    [InlineData("SJRES", "S.J.Res.")]
    public void ATypeLabelIsThePrintedTypeWithNoNumber(string type, string expected) =>
        Assert.Equal(expected, BillNumbers.TypeLabel(type));

    [Theory]
    [InlineData("hr", true)]
    [InlineData("sconres", true)]
    [InlineData("HR", false)]
    [InlineData("hamdt", false)]
    public void OnlyALowerCaseCodeInTheTableIsABillType(string type, bool expected) =>
        Assert.Equal(expected, BillNumbers.IsType(type));
}
