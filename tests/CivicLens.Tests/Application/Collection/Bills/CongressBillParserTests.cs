using System.Text;
using CivicLens.Application.Collection.Bills;

namespace CivicLens.Tests.Application.Collection.Bills;

public sealed class CongressBillParserTests
{
    private static readonly CongressBillListItem Expected = new(119, "hr", 1, new DateOnly(2025, 7, 4));

    private const string Valid = """
        {"bill":{"congress":119,"type":"HR","number":"1","title":"A bill","introducedDate":"2025-01-03",
         "updateDate":"2025-07-04T08:03:25Z","policyArea":{"name":"Taxation"},
         "sponsors":[{"bioguideId":"A000375","url":"https://evil.test/x"}],
         "cosponsors":{"count":2,"countIncludingWithdrawnCosponsors":3,"url":"https://evil.test/c"},"actions":{"count":71,"url":"https://evil.test/a"},
         "summaries":{"count":5,"url":"https://evil.test/s"}},
         "request":{"billNumber":"1"}}
        """;

    [Fact]
    public void ValidDetailYieldsTheFieldsTheGateAndFollowOnJobsNeed()
    {
        var bill = Parse(Valid);

        Assert.Equal((119, "hr", 1, "A bill", new DateOnly(2025, 1, 3)), (bill.Congress, bill.Type, bill.Number, bill.Title, bill.IntroducedDate));
        Assert.Equal(new DateTimeOffset(2025, 7, 4, 8, 3, 25, TimeSpan.Zero), bill.UpdateDate);
        Assert.Equal(("Taxation", 3, 71), (bill.PolicyArea, bill.CosponsorCount, bill.ActionCount));
        Assert.Equal(["A000375"], bill.SponsorBioguideIds);
    }

    [Theory]
    [InlineData("""{"bill":{"congress":119,"type":"HR","number":"1","title":"t","introducedDate":"2025-01-03","updateDate":"2025-07-04T08:03:25Z","sponsors":[],"actions":{"count":0}}}""")]
    [InlineData("""{"bill":{"congress":119,"type":"HR","number":"1","title":"t","introducedDate":"2025-01-03","updateDate":"2025-07-04T08:03:25Z","policyArea":{},"sponsors":[],"actions":{"count":0}}}""")]
    public void MissingPolicyAreaIsValidBecauseALaterGateReasonDependsOnTheAbsence(string json)
    {
        var bill = Parse(json);

        Assert.Null(bill.PolicyArea);
        Assert.Empty(bill.SponsorBioguideIds);
    }

    [Fact]
    public void UpdateDateKeepsTheExactTimestampBecauseTheListOnlyHasADate()
    {
        Assert.Equal(TimeSpan.FromHours(-4), Parse(Valid.Replace("2025-07-04T08:03:25Z", "2025-07-04T08:03:25-04:00")).UpdateDate.Offset);
    }

    [Theory]
    [InlineData("\"updateDate\":\"2025-07-04T08:03:25Z\"", "\"updateDate\":\"2025-07-04\"")]
    [InlineData("\"updateDate\":\"2025-07-04T08:03:25Z\"", "\"updateDate\":\"2025-07-04T08:03:25\"")]
    [InlineData("\"updateDate\":\"2025-07-04T08:03:25Z\",", "")]
    public void UpdateDateWithoutAnExactTimeIsInvalidBecauseTwoUpdatesOnOneDayWouldShareAKey(string original, string replacement)
    {
        Assert.Throws<InvalidDataException>(() => Parse(Valid.Replace(original, replacement)));
    }

    [Theory]
    [InlineData("\"congress\":119", "\"congress\":0")]
    [InlineData("\"number\":\"1\"", "\"number\":0")]
    [InlineData("\"title\":\"A bill\",", "")]
    [InlineData("\"introducedDate\":\"2025-01-03\",", "")]
    [InlineData("\"count\":71", "\"count\":-1")]
    [InlineData("\"bioguideId\":\"A000375\"", "\"bioguideId\":\" \"")]
    [InlineData("\"bioguideId\":\"A000375\"", "\"bioguideId\":\" A000375\"")]
    [InlineData("\"bioguideId\":\"A000375\"", "\"bioguideId\":\"a000375\"")]
    public void AnIncompleteFieldIsInvalidBecauseThePartialBillMustNotReachTheGate(string original, string replacement)
    {
        Assert.Throws<InvalidDataException>(() => Parse(Valid.Replace(original, replacement)));
    }

    [Fact]
    public void TheSummaryCountDecidesWhetherASummariesPageIsFetchedSoAMissingObjectMeansZero()
    {
        Assert.Equal(5, Parse(Valid).SummaryCount);
        Assert.Equal(0, Parse(Valid.Replace("\"summaries\":{\"count\":5,\"url\":\"https://evil.test/s\"}", "\"summaries\":null")).SummaryCount);
    }

    [Theory]
    [InlineData("\"summaries\":{\"count\":5,", "\"summaries\":{\"count\":-1,")]
    [InlineData("\"summaries\":{\"count\":5,", "\"summaries\":{\"count\":null,")]
    [InlineData("\"summaries\":{\"count\":5,", "\"summaries\":{\"count\":\"many\",")]
    public void AnInvalidSummaryCountIsInvalidBecauseItGatesAnExtraFetch(string original, string replacement)
    {
        Assert.Throws<InvalidDataException>(() => Parse(Valid.Replace(original, replacement)));
    }

    [Fact]
    public void AReservedBillNumberHasNoActionObjectSoItParsesWithZeroActionsInsteadOfBlockingTheJob()
    {
        Assert.Equal(0, Parse(Valid.Replace("\"actions\":{\"count\":71,\"url\":\"https://evil.test/a\"},", "")).ActionCount);
    }

    [Fact]
    public void ABillWithoutCosponsorsHasNoCosponsorObjectSoTheCountIsZero()
    {
        Assert.Equal(0, Parse(Valid.Replace("\"cosponsors\":{\"count\":2,\"countIncludingWithdrawnCosponsors\":3,\"url\":\"https://evil.test/c\"},", "")).CosponsorCount);
    }

    [Fact]
    public void TheCosponsorCountIncludesWithdrawnCosponsorsBecauseAWithdrawalIsAlsoAnAction()
    {
        Assert.Equal(3, Parse(Valid).CosponsorCount);
        Assert.Throws<InvalidDataException>(() => Parse(Valid.Replace(",\"countIncludingWithdrawnCosponsors\":3", "")));
    }

    [Fact]
    public void ANumericBillNumberIsReadLikeTheStringForm()
    {
        Assert.Equal(1, Parse(Valid.Replace("\"number\":\"1\"", "\"number\":1")).Number);
    }

    [Fact]
    public void ABlankPolicyAreaNameIsAbsentAndAPaddedOneIsTrimmedSoTheGateMatchesConfiguredNames()
    {
        Assert.Null(Parse(Valid.Replace("\"Taxation\"", "\"  \"")).PolicyArea);
        Assert.Equal("Taxation", Parse(Valid.Replace("\"Taxation\"", "\" Taxation \"")).PolicyArea);
    }

    [Fact]
    public void ARepeatedSponsorIsListedOnceSoTheGateCountsEachPersonOnce()
    {
        var json = Valid.Replace("[{\"bioguideId\":\"A000375\",", "[{\"bioguideId\":\"A000375\"},{\"bioguideId\":\"A000375\",");

        Assert.Equal(["A000375"], Parse(json).SponsorBioguideIds);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("""{"bill":null}""")]
    [InlineData("""{"bill":{}}""")]
    [InlineData("""{"bill":{"congress":119,"type":"HR","number":"1","title":"t","introducedDate":"2025-01-03","updateDate":"2025-07-04T08:03:25Z","actions":{"count":0}}}""")]
    [InlineData("""{"bill":{"congress":119,"type":"HR","number":"1","title":"t","introducedDate":"2025-01-03","updateDate":"2025-07-04T08:03:25Z","sponsors":[null],"actions":{"count":0}}}""")]
    [InlineData("""{"bill":{"congress":119,"type":"HR","number":"1","title":"t","introducedDate":"2025-01-03","updateDate":"2025-07-04T08:03:25Z","sponsors":[{}],"actions":{"count":0}}}""")]
    [InlineData("""{"bill":{"congress":119,"type":"HR","number":"1","title":"t","introducedDate":"2025-01-03","updateDate":"2025-07-04T08:03:25Z","sponsors":[],"actions":{"count":-1}}}""")]
    [InlineData("""{"bill":{"congress":119,"type":"HR","number":"1","title":"t","introducedDate":"2025-01-03","updateDate":"2025-07-04T08:03:25Z","sponsors":[],"cosponsors":{"count":0,"countIncludingWithdrawnCosponsors":-1},"actions":{"count":0}}}""")]
    [InlineData("""{"bill":{"congress":119,"type":"HR","number":"1","title":"t","introducedDate":"soon","updateDate":"2025-07-04T08:03:25Z","sponsors":[],"actions":{"count":0}}}""")]
    [InlineData("""{"bill":{"congress":119,"type":"HR","number":"one","title":"t","introducedDate":"2025-01-03","updateDate":"2025-07-04T08:03:25Z","sponsors":[],"actions":{"count":0}}}""")]
    public void MalformedDetailsAreInvalidDataBecauseAPartialBillMustNotReachTheGate(string json)
    {
        Assert.Throws<InvalidDataException>(() => Parse(json));
    }

    [Fact]
    public void UnknownBillTypeIsInvalidBecauseItCannotBeAConfiguredBillKind()
    {
        Assert.Throws<InvalidDataException>(() => Parse(Valid.Replace("\"HR\"", "\"../x\"")));
    }

    [Theory]
    [InlineData("\"congress\":119", "\"congress\":118")]
    [InlineData("\"type\":\"HR\"", "\"type\":\"S\"")]
    [InlineData("\"number\":\"1\"", "\"number\":\"2\"")]
    public void DetailForAnotherBillIsInvalidBecauseTheJobAskedForOneBill(string original, string replacement)
    {
        Assert.Throws<InvalidDataException>(() => Parse(Valid.Replace(original, replacement)));
    }

    private static CongressBill Parse(string json) => CongressBillParser.Parse(Encoding.UTF8.GetBytes(json), Expected);
}
