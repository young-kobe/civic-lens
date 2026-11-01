using System.Text;
using CivicLens.Application.Collection.Bills;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.BillTextFixtures;

namespace CivicLens.Tests.Application.Collection.Bills;

public sealed class CongressBillTextVersionsParserTests
{
    private static readonly CongressBillListItem Bill = new(119, "hr", 7, default);

    private static CongressBillTextVersionsPage Parse(string json) =>
        CongressBillTextVersionsParser.Parse(Encoding.UTF8.GetBytes(json), Bill);

    [Fact]
    public void TheNewestVersionByDateIsChosenWhateverTheListOrder()
    {
        var introduced = Version("Introduced in House", "2025-03-01T05:00:00Z", "BILLS-119hr7ih");
        var reported = Version("Reported in House", "2025-05-01T04:00:00Z", "BILLS-119hr7rh");

        Assert.Equal("BILLS-119hr7rh", Parse(Versions(introduced, reported)).Newest!.PackageId);
        Assert.Equal("BILLS-119hr7rh", Parse(Versions(reported, introduced)).Newest!.PackageId);
    }

    [Fact]
    public void AVersionWithNoDateLosesToADatedVersionBecauseItsAgeIsUnknown()
    {
        var undated = Version("Public Law", null, "BILLS-119hr7enr");
        var dated = Version("Introduced in House", "2025-03-01T05:00:00Z", "BILLS-119hr7ih");

        var page = Parse(Versions(undated, dated));

        Assert.Equal("BILLS-119hr7ih", page.Newest!.PackageId);
        Assert.Null(page.Versions.Single(version => version.PackageId == "BILLS-119hr7enr").Date);
    }

    [Fact]
    public void WhenEveryDateIsMissingTheFirstListedVersionIsChosen()
    {
        var page = Parse(Versions(Version("Engrossed in House", null, "BILLS-119hr7eh"), Version("Introduced in House", null, "BILLS-119hr7ih")));

        Assert.Equal("BILLS-119hr7eh", page.Newest!.PackageId);
    }

    [Fact]
    public void ANewerVersionWithoutFormattedTextIsSkippedSoTheNewestReadableVersionWins()
    {
        var newerPdfOnly = Version("Reported in House", "2025-05-01T04:00:00Z", "BILLS-119hr7rh", withFormattedText: false);
        var older = Version("Introduced in House", "2025-03-01T05:00:00Z", "BILLS-119hr7ih");

        var page = Parse(Versions(newerPdfOnly, older));

        Assert.Equal("BILLS-119hr7ih", Assert.Single(page.Versions).PackageId);
        Assert.Equal("BILLS-119hr7ih", page.Newest!.PackageId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABillWithNoFormattedTextHasNoNewestVersionSoTheReadEndsAsNoText(bool anyVersionListed)
    {
        var json = anyVersionListed
            ? Versions(Version("Introduced in House", "2025-03-01T05:00:00Z", withFormattedText: false))
            : Versions();

        Assert.Null(Parse(json).Newest);
    }

    [Fact]
    public void TheVersionKeepsItsTypeDateAndTheCongressGovUrlAndTakesThePackageFromTheFileName()
    {
        var version = Assert.Single(Parse(Versions(Version("Introduced in House", "2025-03-01T05:00:00Z"))).Versions);

        Assert.Equal(("BILLS-119hr7ih", "Introduced in House", CongressUrl()), (version.PackageId, version.Type, version.Url));
        Assert.Equal(new DateTimeOffset(2025, 3, 1, 5, 0, 0, TimeSpan.Zero), version.Date);
    }

    [Theory]
    [InlineData("BILLS-119hr70ih")]
    [InlineData("BILLS-119hres7ih")]
    [InlineData("BILLS-118hr7ih")]
    [InlineData("BILLS-119hr7")]
    [InlineData("BILLS-119hr7IH")]
    public void AFileNameThatIsNotAPackageOfThisBillIsInvalidSoOneBillNeverStoresTheTextOfAnother(string packageId) =>
        Assert.Throws<InvalidDataException>(() => Parse(Versions(Version("Introduced in House", "2025-03-01T05:00:00Z", packageId))));

    [Theory]
    [InlineData("""{"textVersions":[{"date":null,"formats":[{"type":"Formatted Text","url":"http://www.congress.gov/119/bills/hr7/BILLS-119hr7ih.htm"}],"type":"X"}]}""")]
    [InlineData("""{"textVersions":[{"date":null,"formats":[{"type":"Formatted Text","url":"https://www.congress.gov/119/bills/hr7/BILLS-119hr7ih.pdf"}],"type":"X"}]}""")]
    [InlineData("""{"textVersions":[{"date":null,"formats":[{"type":"Formatted Text"}],"type":"X"}]}""")]
    [InlineData("""{"textVersions":[{"date":null,"formats":[],"type":" "}]}""")]
    [InlineData("""{"textVersions":[null]}""")]
    [InlineData("""{"textVersions":[{"date":"not a date","formats":[],"type":"X"}]}""")]
    [InlineData("""{"pagination":{}}""")]
    public void AnIncompleteOrMalformedEntryIsInvalidData(string json) =>
        Assert.Throws<InvalidDataException>(() => Parse(json));

    [Fact]
    public void AMorePagesLinkIsInvalidBecauseASecondPageCouldHoldANewerVersion() =>
        Assert.Throws<InvalidDataException>(() =>
            Parse("""{"textVersions":[],"pagination":{"next":"https://api.congress.gov/v3/bill/119/hr/7/text?offset=250"}}"""));

    [Fact]
    public void AVersionTypeLongerThanItsColumnIsInvalid() =>
        Assert.Throws<InvalidDataException>(() => Parse(Versions(Version(new string('x', CongressBillTextVersion.MaximumTypeLength + 1), null))));

    [Fact]
    public void ThePackageUrlReadsBackToItsPackageIdAndNoOtherUrlDoes()
    {
        Assert.Equal("https://api.govinfo.gov/packages/BILLS-119hr7ih/htm", PackageUrl());
        Assert.Equal(PackageId, BillTextUrl.TryReadPackageId(SourceUrl, PackageUrl()));
        Assert.Null(BillTextUrl.TryReadPackageId(SourceUrl, SourceUrl + "/BILLS-119hr7ih/pdf"));
        Assert.Null(BillTextUrl.TryReadPackageId(SourceUrl, SourceUrl + "/BILLS-119hr7ih/htm?x=1"));
        Assert.Null(BillTextUrl.TryReadPackageId(SourceUrl, SourceUrl + "/PLAW-119publ7/htm"));
        Assert.Null(BillTextUrl.TryReadPackageId(SourceUrl, "https://www.congress.gov/packages/BILLS-119hr7ih/htm"));
    }
}
