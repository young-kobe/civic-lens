using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.FederalRegister;

public sealed class FederalRegisterTextTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);
    private static readonly DateOnly Published = new(2026, 10, 28);
    private static readonly string Url = FederalRegisterTextUrl.Body(FederalRegisterFixtures.TextSourceUrl, Published, "2026-22051");

    [Fact]
    public void TheBodyUrlHasTheFormOfBodyHtmlUrlAndReadsBackToItsNumberAndDate()
    {
        Assert.Equal("https://www.federalregister.gov/documents/full_text/html/2026/10/28/2026-22051.html", Url);
        Assert.Equal(("2026-22051", Published), FederalRegisterTextUrl.Read(FederalRegisterFixtures.TextSourceUrl, Url));
    }

    [Theory]
    [InlineData("https://www.federalregister.gov/documents/full_text/html/2026/10/28/2026-22051.html?x=1")]
    [InlineData("https://www.federalregister.gov/documents/full_text/xml/2026/10/28/2026-22051.xml")]
    [InlineData("https://www.federalregister.gov/documents/full_text/text/2026/10/28/2026-22051.txt")]
    [InlineData("https://www.federalregister.gov/documents/full_text/html/2026/10/28/2026-22051.json")]
    [InlineData("https://www.federalregister.gov/documents/full_text/html/2026/10/28/../2026-22051.html")]
    [InlineData("https://www.federalregister.gov/documents/full_text/html/2026/10/32/2026-22051.html")]
    [InlineData("https://www.federalregister.gov/documents/full_text/html/2026/1/28/2026-22051.html")]
    [InlineData("https://www.federalregister.gov/documents/full_text/html/2026/10/28/not a number.html")]
    [InlineData("https://www.federalregister.gov/documents/full_text/html/2026/10/28/2026-22051/slug.html")]
    [InlineData("https://www.federalregister.gov/documents/2026/10/28/2026-22051/a-title")]
    [InlineData("https://www.federalregister.gov/api/v1/documents/2026-22051.json")]
    [InlineData("https://evil.example/documents/full_text/html/2026/10/28/2026-22051.html")]
    public void AnyOtherUrlFormIsRefusedAsAJobOfTheTextSource(string url)
    {
        var source = new ConfiguredCollectionSource(FederalRegisterFixtures.Configuration(withTextSource: true), FederalRegisterFixtures.TextSourceId, Today);

        Assert.Null(FederalRegisterTextUrl.TryRead(FederalRegisterFixtures.TextSourceUrl, url));
        Assert.Throws<ArgumentException>(() => source.CreateApiJobDefinition(ApiResource.FederalRegisterText, url, Today, CollectionJobPriority.Check));
    }

    [Fact]
    public void ADocumentUrlOrAListUrlIsRefusedForTheTextResourceAndTheBodyUrlIsRefusedForTheDocumentResource()
    {
        var text = new ConfiguredCollectionSource(FederalRegisterFixtures.Configuration(withTextSource: true), FederalRegisterFixtures.TextSourceId, Today);
        var documents = new ConfiguredCollectionSource(FederalRegisterFixtures.Configuration(withTextSource: true), FederalRegisterFixtures.SourceId, Today);

        _ = text.CreateApiJobDefinition(ApiResource.FederalRegisterText, Url, Today, CollectionJobPriority.Check);
        Assert.Throws<ArgumentException>(() => text.CreateApiJobDefinition(ApiResource.FederalRegisterDocument,
            FederalRegisterUrl.Document(FederalRegisterFixtures.SourceUrl, "2026-22051"), Today, CollectionJobPriority.Check));
        Assert.Throws<ArgumentException>(() => documents.CreateApiJobDefinition(ApiResource.FederalRegisterDocument, Url, Today, CollectionJobPriority.Check));
        Assert.Throws<ArgumentException>(() => documents.CreateApiJobDefinition(ApiResource.FederalRegisterText, Url, Today, CollectionJobPriority.Check));
    }

    [Fact]
    public void ATextJobUsesTheKeyOfItsDocumentTheTwoMegabyteBudgetAndNoCredential()
    {
        var source = new ConfiguredCollectionSource(FederalRegisterFixtures.Configuration(withTextSource: true), FederalRegisterFixtures.TextSourceId, Today);

        var job = Assert.Single(source.CreateFederalRegisterTextBatch("2026-22051", Published, Today).Jobs);

        Assert.Equal("federal-register/federal-register-text/text/2026-22051", job.IdempotencyKey);
        Assert.Equal(Url, job.Definition.Url);
        Assert.Equal(2_000_000, job.Definition.MaxBytes);
        Assert.Null(job.Definition.Credential);
        Assert.Null(source.CreateScheduledJobDefinition(DateTimeOffset.UnixEpoch, null));
    }

    [Fact]
    public void ACredentialAValidatorACoverageListOrABackfillDateIsRefusedForTheTextSource()
    {
        var configuration = FederalRegisterFixtures.Configuration(withTextSource: true);
        var text = FederalRegisterFixtures.TextSource();
        WatchedSourceConfiguration[] changes =
        [
            text with { Credential = CollectionCredentials.CongressGov },
            text with { ETag = "\"x\"" },
            text with { MinimumCongress = 119 },
            text with { BackfillFrom = FederalRegisterFixtures.Backfill }
        ];

        configuration.Validate();
        Assert.All(changes, changed => Assert.Throws<ArgumentException>(() =>
            (configuration with { Sources = [configuration.Sources[0], changed] }).Validate()));
        Assert.Empty(configuration.CoveredPersonIdsOf(configuration.Sources[1], Today));
    }

    [Fact]
    public void ATextSourceThatCannotHoldOneJobIsRefused()
    {
        var configuration = FederalRegisterFixtures.Configuration(withTextSource: true);
        var tooSmall = FederalRegisterFixtures.TextSource() with { AdmissionPolicy = new() { MaxJobs = 1, MaxTotalRequests = 1 } };

        Assert.Throws<ArgumentException>(() => (configuration with { Sources = [configuration.Sources[0], tooSmall] }).Validate());
    }

    [Fact]
    public void TextOfTheWindowLengthOrLessStaysWholeAndTheHeaderCountsIt()
    {
        var reading = FederalRegisterRenderer.RenderText("2026-22051", FederalRegisterFixtures.OrderBodyText);

        Assert.Equal(FederalRegisterFixtures.OrderBodyText.Length, reading.CharsRead);
        Assert.Equal(reading.CharsRead, reading.CharsTotal);
        Assert.Equal("Federal Register document: 2026-22051\n" +
            $"Characters read: {reading.CharsRead} of {reading.CharsRead}\nText:\n{FederalRegisterFixtures.OrderBodyText}", reading.Text);
    }

    [Fact]
    public void TextOverTheWindowKeepsTheFirstWindowCutAtASpaceAndTheHonestTotal()
    {
        var text = string.Join(' ', Enumerable.Range(1, 20_000).Select(number => $"word{number}"));

        var reading = FederalRegisterRenderer.RenderText("2026-22051", text);

        Assert.True(text.Length > CongressBillRenderer.TextWindowLength);
        Assert.Equal(text.Length, reading.CharsTotal);
        Assert.True(reading.CharsRead is > 0 and <= CongressBillRenderer.TextWindowLength && text[reading.CharsRead] == ' ');
        Assert.StartsWith($"Federal Register document: 2026-22051\nCharacters read: {reading.CharsRead} of {text.Length}\nText:\n", reading.Text);
        Assert.EndsWith(text[..reading.CharsRead], reading.Text);
    }

    [Fact]
    public void EmptyTextAndTextWithNoSpaceInsideTheWindowAreInvalidData()
    {
        Assert.Throws<InvalidDataException>(() => FederalRegisterRenderer.RenderText("2026-22051", ""));
        Assert.Throws<InvalidDataException>(() =>
            FederalRegisterRenderer.RenderText("2026-22051", new string('x', CongressBillRenderer.TextWindowLength + 1)));
    }
}
