using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.FederalRegister;

public sealed class FederalRegisterParserTests
{
    [Fact]
    public void AListHoldsEachDocumentNumberWithItsPublicationDateSoPriorityCanFollowTheDay()
    {
        var page = FederalRegisterListParser.Parse(FederalRegisterFixtures.Bytes(FederalRegisterFixtures.List));

        Assert.Equal(["2025-01900", "2025-01901", "2025-01902"], page.Items.Select(item => item.DocumentNumber));
        Assert.All(page.Items, item => Assert.Equal(new DateOnly(2025, 1, 28), item.PublishedOn));
    }

    [Fact]
    public void AListOfMoreThanOnePageIsInvalidBecauseTheWindowMustFitOneRequest()
    {
        var json = FederalRegisterFixtures.List.Replace("\"total_pages\":1", "\"total_pages\":2");

        Assert.Throws<InvalidDataException>(() => FederalRegisterListParser.Parse(FederalRegisterFixtures.Bytes(json)));
    }

    [Fact]
    public void AnEmptyListMayOmitItsResultsButAListWithCountsMustHoldThem()
    {
        var empty = """{"count":0,"total_pages":0,"description":"none"}""";

        Assert.Empty(FederalRegisterListParser.Parse(FederalRegisterFixtures.Bytes(empty)).Items);
        Assert.Throws<InvalidDataException>(() => FederalRegisterListParser.Parse(FederalRegisterFixtures.Bytes("""{"count":2,"total_pages":1}""")));
    }

    [Fact]
    public void AListThatRepeatsADocumentIsInvalid()
    {
        var json = FederalRegisterFixtures.List.Replace("2025-01901", "2025-01900");

        Assert.Throws<InvalidDataException>(() => FederalRegisterListParser.Parse(FederalRegisterFixtures.Bytes(json)));
    }

    [Fact]
    public void AnOrderKeepsItsNumberTitleDatesCitationAndPresident()
    {
        var document = FederalRegisterFixtures.Document(FederalRegisterFixtures.TrumpOrder);

        Assert.Equal(ExecutiveActionKind.ExecutiveOrder, document.Kind);
        Assert.Equal("14147", document.PresidentialNumber);
        Assert.Equal("Ending the Weaponization of the Federal Government", document.Title);
        Assert.Equal(new DateOnly(2025, 1, 20), document.SignedOn);
        Assert.Equal(new DateOnly(2025, 1, 28), document.PublishedOn);
        Assert.Equal("90 FR 8235", document.Citation);
        Assert.Equal("donald-trump", document.PresidentSlug);
        Assert.Equal("2025-01900", document.RecordDocumentNumber);
    }

    [Fact]
    public void ANoticeHasNoPresidentialNumber()
    {
        Assert.Null(FederalRegisterFixtures.Document(FederalRegisterFixtures.BidenNotice).PresidentialNumber);
    }

    [Fact]
    public void ACorrectionNamesTheDocumentItCorrectsAndSharesItsRecord()
    {
        var correction = FederalRegisterFixtures.Document(FederalRegisterFixtures.BidenProclamationCorrection);

        Assert.Equal("2024-26714", correction.CorrectionOf);
        Assert.Equal("R1-2024-26714", correction.DocumentNumber);
        Assert.Equal("2024-26714", correction.RecordDocumentNumber);
    }

    [Fact]
    public void ACorrectionRendersOneNoticeLineThatNamesItsNumberDateAndLink()
    {
        var text = FederalRegisterRenderer.Render(FederalRegisterFixtures.Document(FederalRegisterFixtures.BidenProclamationCorrection));

        Assert.EndsWith("\nCorrection notice: R1-2024-26714, published 2024-11-20, https://www.federalregister.gov/documents/2024/11/20/R1-2024-26714/to-facilitate-positive-adjustment-to-competition-from-imports-of-fine-denier-polyester-staple-fiber", text);
    }

    [Fact]
    public void ARenderedDocumentHasOneFactOnEachLineSoEachFactIsACleanCitation()
    {
        var text = FederalRegisterRenderer.Render(FederalRegisterFixtures.Document(FederalRegisterFixtures.TrumpOrder));

        Assert.Equal("""
            Federal Register document: 2025-01900
            Document type: Executive Order
            Presidential document number: 14147
            Title: Ending the Weaponization of the Federal Government
            Signed: 2025-01-20
            Published: 2025-01-28
            Citation: 90 FR 8235
            President: Donald Trump
            Document URL: https://www.federalregister.gov/documents/2025/01/28/2025-01900/ending-the-weaponization-of-the-federal-government
            """.ReplaceLineEndings("\n"), text);
    }

    [Fact]
    public void AResponseForAnotherDocumentIsInvalidBecauseTheJobUrlNamesOneDocument()
    {
        Assert.Throws<InvalidDataException>(() => FederalRegisterDocumentParser.Parse(
            FederalRegisterFixtures.Bytes(FederalRegisterFixtures.TrumpOrder), "2025-01901"));
    }

    [Theory]
    [InlineData("\"subtype\":\"Executive Order\"", "\"subtype\":\"Determination\"")]
    [InlineData("\"type\":\"Presidential Document\"", "\"type\":\"Rule\"")]
    [InlineData("\"signing_date\":\"2025-01-20\"", "\"signing_date\":null")]
    [InlineData("\"title\":\"Ending the Weaponization of the Federal Government\"", "\"title\":\" \"")]
    [InlineData("\"president\":{\"identifier\":\"donald-trump\",\"name\":\"Donald Trump\"}", "\"president\":null")]
    [InlineData("https://www.federalregister.gov/documents/2025/01/28", "https://example.test/documents/2025/01/28")]
    [InlineData("\"correction_of\":null", "\"correction_of\":\"not a document\"")]
    public void ADocumentThatLacksAnyRequiredFactOrHasAnUnknownTypeIsInvalid(string find, string replace)
    {
        var json = FederalRegisterFixtures.TrumpOrder.Replace(find, replace);

        Assert.Throws<InvalidDataException>(() => FederalRegisterDocumentParser.Parse(FederalRegisterFixtures.Bytes(json), "2025-01900"));
    }

    [Fact]
    public void ATitleWithLineBreaksBecomesOneLine()
    {
        var json = FederalRegisterFixtures.TrumpOrder.Replace("Ending the Weaponization", "Ending\\nthe Weaponization");

        var document = FederalRegisterDocumentParser.Parse(FederalRegisterFixtures.Bytes(json), "2025-01900");

        Assert.Equal("Ending the Weaponization of the Federal Government", document.Title);
    }
}
