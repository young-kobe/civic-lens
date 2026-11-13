using System.Text.Json;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PublishedExecutiveActionTests
{
    private const string Text = "Federal Register document: 2025-01900\nTitle: Ending the Weaponization\nSigned: 2025-01-20\nPublished: 2025-01-28\nCitation: 90 FR 8235\nDocument URL: https://www.federalregister.gov/documents/2025/01/28/2025-01900/x\nCorrection notice: R1-2025-00001, published 2025-02-03, https://www.federalregister.gov/x";
    private static readonly string Id = new('a', 64);

    [Fact]
    public void ARecordFileUsesTheCamelCaseKindAndTheFactShape()
    {
        var json = JsonSerializer.Serialize(Record(), PublicationProtocol.JsonOptions);

        Assert.Contains("\"kind\":\"executiveOrder\"", json);
        Assert.Contains("\"facts\":[{\"label\":\"Federal Register document\",\"value\":\"2025-01900\",\"citation\":{", json);
        Assert.Contains("\"kind\":\"executiveAction\"", JsonSerializer.Serialize(Record().ToEntry(), PublicationProtocol.JsonOptions));
    }

    [Fact]
    public void AValidRecordPassesAndItsEntryDatesItBySigning()
    {
        Record().Validate();

        var entry = Record().ToEntry();
        entry.Validate();
        Assert.Equal(new DateOnly(2025, 1, 20), entry.EventDate);
        Assert.Equal(["o1"], entry.Officials.Select(official => official.Id));
    }

    [Fact]
    public void TheLabelsThatTheContractChecksEqualTheLabelsThatCodeWrites()
    {
        Assert.Equal(ExecutiveActionFacts.DocumentNumber, PublishedExecutiveAction.DocumentNumberLabel);
        Assert.Equal(ExecutiveActionFacts.Title, PublishedExecutiveAction.TitleLabel);
        Assert.Equal(ExecutiveActionFacts.Signed, PublishedExecutiveAction.SignedLabel);
        Assert.Equal(ExecutiveActionFacts.Published, PublishedExecutiveAction.PublishedLabel);
        Assert.Equal(ExecutiveActionFacts.Citation, PublishedExecutiveAction.CitationLabel);
        Assert.Equal(ExecutiveActionFacts.CorrectionNotice, PublishedExecutiveAction.CorrectionNoticeLabel);
        Assert.Equal(ExecutiveActionFacts.Url, PublishedExecutiveAction.UrlLabel);
    }

    [Fact]
    public void AHeadlineThatIsNotTheCitedTitleIsRefusedSoTheOfficialTitleCannotDrift()
    {
        Assert.Throws<InvalidDataException>((Record() with { Headline = "A friendlier title" }).Validate);
    }

    [Fact]
    public void ASigningDateThatTheEvidenceDoesNotShowIsRefused()
    {
        Assert.Throws<InvalidDataException>((Record() with { SignedOn = new DateOnly(2025, 1, 21) }).Validate);
    }

    [Fact]
    public void AFactWhoseQuoteIsNotItsLabelAndValueIsRefused()
    {
        var facts = Record().Facts;
        facts[0] = facts[0] with { Value = "2025-99999" };

        Assert.Throws<InvalidDataException>(Record(facts).Validate);
    }

    [Fact]
    public void ACorrectionNoticeNeedsItsCitedFactAndAFactNeedsItsNotice()
    {
        var notice = new PublishedExecutiveActionNotice { DocumentNumber = "R1-2025-00001", PublishedOn = new DateOnly(2025, 2, 3), Url = "https://www.federalregister.gov/x" };

        Assert.Throws<InvalidDataException>((Record() with { CorrectionNotices = [notice] }).Validate);
        Assert.Throws<InvalidDataException>(Record([.. Record().Facts, NoticeFact(notice)]).Validate);
        Assert.Equal("R1-2025-00001, published 2025-02-03, https://www.federalregister.gov/x", notice.Value);
    }

    [Fact]
    public void TwoNoticesOfTheSameDocumentAreRefused()
    {
        var notice = new PublishedExecutiveActionNotice { DocumentNumber = "R1-2025-00001", PublishedOn = new DateOnly(2025, 2, 3), Url = "https://www.federalregister.gov/x" };
        var facts = Record().Facts;

        Assert.Throws<InvalidDataException>((Record([.. facts, NoticeFact(notice), NoticeFact(notice)]) with { CorrectionNotices = [notice, notice] }).Validate);
    }

    [Fact]
    public void TheEntryNamesTheDocumentTypeSoThePresidentPageShowsTheSpecificBadgeAndOtherKindsNameNone()
    {
        var entry = Record().ToEntry();
        entry.Validate();

        Assert.Equal(PublishedExecutiveActionKind.ExecutiveOrder, entry.DocumentType);
        Assert.Throws<InvalidDataException>((entry with { DocumentType = null }).Validate);
        Assert.Throws<InvalidDataException>((entry with { Kind = PublishedRecordKind.Vote }).Validate);
    }

    private const string BodyText = "Header\nBy the authority vested in me, I order each agency head to act within 30 days.\nEach agency shall report the results to the President.";
    private static readonly string BodyId = new('b', 64);

    [Fact]
    public void ASummaryThatQuotesItsOwnSourceOfTheRecordPasses()
    {
        WithSummary().Validate();
    }

    [Fact]
    public void ASummaryThatQuotesAnExtractionThatIsNoSourceOfTheRecordIsRefusedSoAForeignSpanNeverPublishes()
    {
        var foreign = WithSummary();
        var summary = foreign.Summary! with { Sentences = [.. foreign.Summary.Sentences.Select(sentence => sentence with { Citation = sentence.Citation with { ExtractionId = new string('f', 64) } })] };

        Assert.Throws<InvalidDataException>((foreign with { Summary = summary }).Validate);
    }

    [Fact]
    public void ASummaryWithOneSentenceOrWithQuotesOfTwoExtractionsIsRefused()
    {
        var record = WithSummary();
        var sentences = record.Summary!.Sentences;

        Assert.Throws<InvalidDataException>((record with { Summary = record.Summary with { Sentences = sentences[..1] } }).Validate);
        var mixed = new[] { sentences[0], sentences[1] with { Citation = sentences[1].Citation with { ExtractionId = ExtractionId } } };
        Assert.Throws<InvalidDataException>((record with { Summary = record.Summary with { Sentences = mixed } }).Validate);
    }

    [Fact]
    public void ASummaryQuoteThatIsNotTheTextAtItsOffsetsIsRefused()
    {
        var record = WithSummary();
        var sentences = record.Summary!.Sentences;
        sentences[0] = sentences[0] with { Citation = sentences[0].Citation with { Start = sentences[0].Citation.Start + 1 } };

        Assert.Throws<InvalidDataException>(record.Validate);
    }

    [Fact]
    public void ASummaryThatIsNotModelTextIsRefusedAndAnAbsentSummaryWritesNoField()
    {
        var record = WithSummary();

        Assert.Throws<InvalidDataException>((record with { Summary = record.Summary! with { TextOrigin = PublishedTextOrigin.Person } }).Validate);
        Assert.DoesNotContain("summary", JsonSerializer.Serialize(Record(), PublicationProtocol.JsonOptions));
        Assert.Contains("\"summary\":{\"textOrigin\":\"model\",\"cut\":true", JsonSerializer.Serialize(record, PublicationProtocol.JsonOptions));
    }

    [Fact]
    public void ASummaryFitsBesideTheMostCorrectionNoticesBecauseTheBodyTextIsOneMoreSource()
    {
        var record = WithSummary();
        var notices = Enumerable.Range(1, PublishedExecutiveAction.MaximumNotices).Select(number => new PublishedExecutiveActionNotice
        {
            DocumentNumber = $"R1-2025-{number:D5}",
            PublishedOn = new DateOnly(2025, 2, number),
            Url = $"https://www.federalregister.gov/d/R1-2025-{number:D5}"
        }).ToArray();
        var texts = notices.Select(notice => $"{PublishedExecutiveAction.CorrectionNoticeLabel}: {notice.Value}").ToArray();
        var ids = notices.Select((_, index) => new string((char)('1' + index), 64)).ToArray();
        var full = record with
        {
            CorrectionNotices = notices,
            Facts = [.. record.Facts, .. notices.Select((notice, index) => new PublishedExecutiveActionFact
            {
                Label = PublishedExecutiveAction.CorrectionNoticeLabel, Value = notice.Value,
                Citation = new() { ExtractionId = ids[index], Start = 0, Length = texts[index].Length, Quote = texts[index] }
            })],
            Sources = [.. record.Sources, .. texts.Select((text, index) => new PublishedDocumentVersion
            {
                ExtractionId = ids[index], Url = notices[index].Url, ObservedAtUtc = DateTimeOffset.UnixEpoch, Text = text
            })]
        };

        full.Validate();
        Assert.Equal(PublishedExecutiveAction.MaximumSources, full.Sources.Length);
    }

    private static PublishedExecutiveAction WithSummary()
    {
        PublishedExecutiveSentence Sentence(string sentence, string quote) => new()
        {
            Sentence = sentence,
            Citation = new() { ExtractionId = BodyId, Start = BodyText.IndexOf(quote, StringComparison.Ordinal), Length = quote.Length, Quote = quote }
        };
        var record = Record();
        return record with
        {
            Sources = [.. record.Sources, new() { ExtractionId = BodyId, Url = "https://www.federalregister.gov/documents/full_text/html/2025/01/28/2025-01900.html", ObservedAtUtc = DateTimeOffset.UnixEpoch, Text = BodyText }],
            Summary = new()
            {
                TextOrigin = PublishedTextOrigin.Model,
                Cut = true,
                Sentences =
                [
                    Sentence("The order tells each agency head to act within 30 days.", "I order each agency head to act within 30 days"),
                    Sentence("Each agency reports the results to the President.", "Each agency shall report the results to the President")
                ]
            }
        };
    }

    private static readonly string ExtractionId = new('e', 64);

    private static PublishedExecutiveActionFact Fact(string label, string value)
    {
        var line = $"{label}: {value}";
        return new() { Label = label, Value = value, Citation = new() { ExtractionId = ExtractionId, Start = Text.IndexOf(line, StringComparison.Ordinal), Length = line.Length, Quote = line } };
    }

    private static PublishedExecutiveActionFact NoticeFact(PublishedExecutiveActionNotice notice) => Fact(PublishedExecutiveAction.CorrectionNoticeLabel, notice.Value);

    private static PublishedExecutiveAction Record(PublishedExecutiveActionFact[]? facts = null)
    {
        return new()
        {
            RecordId = Id,
            RevisionNumber = 1,
            Headline = "Ending the Weaponization",
            Kind = PublishedExecutiveActionKind.ExecutiveOrder,
            DocumentNumber = "2025-01900",
            SignedOn = new DateOnly(2025, 1, 20),
            PublishedOn = new DateOnly(2025, 1, 28),
            FederalRegisterCitation = "90 FR 8235",
            DocumentUrl = "https://www.federalregister.gov/documents/2025/01/28/2025-01900/x",
            President = new() { Id = "o1", Name = "Jane Doe" },
            Facts = facts ?? [Fact("Federal Register document", "2025-01900"), Fact("Title", "Ending the Weaponization"), Fact("Signed", "2025-01-20"),
                Fact("Published", "2025-01-28"), Fact("Citation", "90 FR 8235"),
                Fact("Document URL", "https://www.federalregister.gov/documents/2025/01/28/2025-01900/x")],
            Sources = [new() { ExtractionId = ExtractionId, Url = "https://www.federalregister.gov/api/v1/documents/2025-01900.json", ObservedAtUtc = DateTimeOffset.UnixEpoch, Text = Text }],
            ApprovedAtUtc = DateTimeOffset.UnixEpoch,
            FirstPublishedAtUtc = DateTimeOffset.UnixEpoch,
            CorrectionNotices = [],
            Corrections = []
        };
    }
}
