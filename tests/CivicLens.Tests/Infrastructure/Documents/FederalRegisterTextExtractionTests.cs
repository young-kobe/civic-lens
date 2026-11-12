using System.Text;
using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Infrastructure.Documents;

public sealed class FederalRegisterTextExtractionTests
{
    [Fact]
    public async Task ThePrintedPageMarkersNeverReachTheTextButTheOrderAndItsEndMatterDo()
    {
        await using var capture = await TestCapture.CreateAsync(Encoding.UTF8.GetBytes(FederalRegisterFixtures.OrderBodyHtml), "text/html; charset=utf-8");

        var text = await capture.Extractor.ExtractAsync(capture.Attempt, capture.Root, FederalRegisterTextParser.Profile, CancellationToken.None);

        Assert.DoesNotContain("printed page", text, StringComparison.Ordinal);
        Assert.DoesNotContain("68007", text, StringComparison.Ordinal);
        Assert.Equal(FederalRegisterFixtures.OrderBodyText + " " + FederalRegisterFixtures.OrderEndMatterText, Flat(text));
    }

    [Fact]
    public async Task WithNoProfileTheSameBodyKeepsTheMarkersSoTheProfileIsWhatRemovesThem()
    {
        await using var capture = await TestCapture.CreateAsync(Encoding.UTF8.GetBytes(FederalRegisterFixtures.OrderBodyHtml), "text/html; charset=utf-8");

        var text = await capture.Extractor.ExtractAsync(capture.Attempt, capture.Root, null, CancellationToken.None);

        Assert.Contains("printed page 68007", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheExtractionVersionNamesTheTextRuleAndTheExtractor()
    {
        await using var capture = await TestCapture.CreateAsync(Encoding.UTF8.GetBytes(FederalRegisterFixtures.OrderBodyHtml), "text/html");

        Assert.Equal("federal-register-text-v1+html-text-v1", FederalRegisterTextParser.ExtractionVersion(capture.Extractor));
    }

    private static string Flat(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
