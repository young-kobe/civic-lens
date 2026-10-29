using System.Text.Json;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PublishedExecutiveActionTests
{
    private const string Text = "Federal Register document: 2025-01900\nTitle: Ending the Weaponization\nSigned: 2025-01-20\nPublished: 2025-01-28\nCitation: 90 FR 8235\nDocument URL: https://www.federalregister.gov/documents/2025/01/28/2025-01900/x";
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
        Assert.Equal(ExecutiveActionFacts.CorrectionOf, PublishedExecutiveAction.CorrectionOfLabel);
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
    public void ACorrectionNeedsItsCitedFactAndOtherRecordsHaveNone()
    {
        Assert.Throws<InvalidDataException>((Record() with { CorrectionOf = "2024-26714" }).Validate);
    }

    private static PublishedExecutiveAction Record(PublishedExecutiveActionFact[]? facts = null)
    {
        var extractionId = new string('e', 64);
        PublishedExecutiveActionFact Fact(string label, string value)
        {
            var line = $"{label}: {value}";
            return new() { Label = label, Value = value, Citation = new() { ExtractionId = extractionId, Start = Text.IndexOf(line, StringComparison.Ordinal), Length = line.Length, Quote = line } };
        }
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
            Sources = [new() { ExtractionId = extractionId, Url = "https://www.federalregister.gov/api/v1/documents/2025-01900.json", ObservedAtUtc = DateTimeOffset.UnixEpoch, Text = Text }],
            ApprovedAtUtc = DateTimeOffset.UnixEpoch,
            FirstPublishedAtUtc = DateTimeOffset.UnixEpoch,
            Corrections = []
        };
    }
}
