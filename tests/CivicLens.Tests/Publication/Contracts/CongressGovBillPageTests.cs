using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class CongressGovBillPageTests
{
    [Theory]
    [InlineData("hr", 119, 5334, "https://www.congress.gov/bill/119th-congress/house-bill/5334")]
    [InlineData("s", 118, 55, "https://www.congress.gov/bill/118th-congress/senate-bill/55")]
    [InlineData("hjres", 111, 2, "https://www.congress.gov/bill/111th-congress/house-joint-resolution/2")]
    [InlineData("sjres", 101, 7, "https://www.congress.gov/bill/101st-congress/senate-joint-resolution/7")]
    [InlineData("hconres", 102, 14, "https://www.congress.gov/bill/102nd-congress/house-concurrent-resolution/14")]
    [InlineData("sconres", 112, 8, "https://www.congress.gov/bill/112th-congress/senate-concurrent-resolution/8")]
    [InlineData("hres", 103, 9, "https://www.congress.gov/bill/103rd-congress/house-resolution/9")]
    [InlineData("sres", 113, 3, "https://www.congress.gov/bill/113th-congress/senate-resolution/3")]
    public void TheBillPageAddressNamesTheOrdinalAndTheSlugOfTheBillType(string type, int congress, int number, string expected) =>
        Assert.Equal(expected, CongressGovBillPage.Url(congress, type, number));

    [Fact]
    public void ABillTypeOutsideTheTableIsRefused() =>
        Assert.Throws<KeyNotFoundException>(() => CongressGovBillPage.Url(119, "hamdt", 1));
}
