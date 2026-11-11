using CivicLens.Application.Actions;
using CivicLens.PublicApi.Bills;

namespace CivicLens.Tests.PublicApi.Bills;

public sealed class BillNumberLabelTests
{
    [Theory]
    [InlineData("hr")]
    [InlineData("s")]
    [InlineData("hjres")]
    [InlineData("sjres")]
    [InlineData("hconres")]
    [InlineData("sconres")]
    [InlineData("hres")]
    [InlineData("sres")]
    [InlineData("xyz")]
    public void ThePublicApiTableMatchesTheApplicationTableForEveryBillType(string billType)
    {
        Assert.Equal(ActionCodeText.BillNumber(billType, 5334), BillNumberLabel.Of(billType, 5334));
    }
}
