using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Core.Documents;
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
    public void DispositionNotesAreOptionalAndKeepTheirExactMultilineValue()
    {
        var notes = "Amends: Space Policy Directive 3, June 18, 2018\r\nRevokes: EO 14056, December 1, 2021\r\nSee: EO 14186, January 27, 2025; EO 14271, April 15, 2025";
        var withNotes = FederalRegisterFixtures.TrumpOrder.Replace("\"html_url\":", $"\"disposition_notes\":{System.Text.Json.JsonSerializer.Serialize(notes)},\"html_url\":");
        var withNull = FederalRegisterFixtures.TrumpOrder.Replace("\"html_url\":", "\"disposition_notes\":null,\"html_url\":");

        Assert.Equal(notes, FederalRegisterFixtures.Document(withNotes).DispositionNotes);
        Assert.Null(FederalRegisterFixtures.Document(withNull).DispositionNotes);
        Assert.Null(FederalRegisterFixtures.Document(FederalRegisterFixtures.TrumpOrder).DispositionNotes);
    }

    [Fact]
    public void ADispositionNotesValueMustBeTextAndFitTheExistingExtractionLimit()
    {
        var wrongType = FederalRegisterFixtures.TrumpOrder.Replace("\"html_url\":", "\"disposition_notes\":17,\"html_url\":");
        var tooLong = FederalRegisterFixtures.TrumpOrder.Replace("\"html_url\":",
            $"\"disposition_notes\":{System.Text.Json.JsonSerializer.Serialize(new string('x', DocumentExtraction.MaximumTextLength + 1))},\"html_url\":");

        Assert.Throws<InvalidDataException>(() => FederalRegisterFixtures.Document(wrongType));
        Assert.Throws<InvalidDataException>(() => FederalRegisterFixtures.Document(tooLong));
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
    public void DispositionNotesAppendAfterTheOfficialFactsWithoutChangingThem()
    {
        const string notes = "Amends: SPD 3\r\nRevokes: EO 14056\r\nSee: EO 14186";
        var json = FederalRegisterFixtures.TrumpOrder.Replace("\"html_url\":", $"\"disposition_notes\":{System.Text.Json.JsonSerializer.Serialize(notes)},\"html_url\":");

        var text = FederalRegisterRenderer.Render(FederalRegisterFixtures.Document(json));
        var facts = FederalRegisterRenderer.Render(FederalRegisterFixtures.Document(FederalRegisterFixtures.TrumpOrder));

        Assert.Equal(facts + "\nDisposition notes: " + notes, text);
    }

    [Fact]
    public void ACorrectionRendersDispositionNotesBeforeItsExistingNoticeLine()
    {
        const string notes = "Amends: SPD 3\nRevokes: EO 14056";
        var json = FederalRegisterFixtures.BidenProclamationCorrection.Replace("\"html_url\":", $"\"disposition_notes\":{System.Text.Json.JsonSerializer.Serialize(notes)},\"html_url\":");

        var text = FederalRegisterRenderer.Render(FederalRegisterFixtures.Document(json));

        Assert.Contains("\nDisposition notes: Amends: SPD 3\nRevokes: EO 14056\nCorrection notice:", text);
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
