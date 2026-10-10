using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Documents;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;
using static CivicLens.Tests.Fixtures.CollectionFixtures;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class EvidencePagesTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly ReviewWorkspaceHost host = new(postgres);

    public async Task InitializeAsync()
    {
        await host.StartAsync();
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|owner"));
    }

    public Task DisposeAsync() => host.StopAsync();

    // Saved text is evidence: it must reach the reader exactly, with CR and C1 code points intact, and never as markup.
    [Fact]
    public async Task SavedTextIsEncodedExactlyAndReadingDoesNotWrite()
    {
        const string text = "\nFirst line\r\n<script>alert('source')</script>\nA \U0001F600 quotation.\u0080\u0085\u0091";
        var extraction = await host.SaveExtractionAsync(text);

        using var response = await host.Client.GetAsync("/Documents/Extraction?extractionId=" + extraction.ExtractionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<script>alert", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        var document = await new HtmlParser().ParseDocumentAsync(html);
        Assert.Equal(text, document.QuerySelector(".doc-text")!.TextContent);
        Assert.Contains(extraction.ExtractionId, html, StringComparison.Ordinal);
        Assert.All(document.QuerySelectorAll("script"), script =>
        {
            Assert.Empty(script.TextContent);
            Assert.NotNull(script.GetAttribute("src"));
        });
        Assert.True(response.Headers.CacheControl?.NoStore);
        var policy = string.Join("; ", response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("form-action 'self'", policy, StringComparison.Ordinal);
        Assert.Equal((1L, 0L), await CountEvidenceAsync());
    }

    // A citation must quote exactly the requested UTF-16 span and mark that same span in the full text.
    [Fact]
    public async Task CitationQuotesTheExactSpanAndRejectsSpansOutsideTheText()
    {
        const string text = "Before \U0001F600 after.\r\nEnd.";
        var extraction = await host.SaveExtractionAsync(text);
        var start = text.IndexOf("\U0001F600", StringComparison.Ordinal);
        var url = $"/Documents/Citation?extractionId={extraction.ExtractionId}";

        var page = await host.GetDocumentAsync($"{url}&start={start}&length=2");

        Assert.Equal("\U0001F600", page.QuerySelector(".quote")!.TextContent);
        Assert.Equal("\U0001F600", page.QuerySelector("mark#cited-passage")!.TextContent);
        Assert.Equal(text, page.QuerySelector(".doc-text")!.TextContent);
        await AssertStatusAsync(HttpStatusCode.BadRequest, $"{url}&start={start}&length=1", "This passage is not in the saved text");
        await AssertStatusAsync(HttpStatusCode.BadRequest, $"{url}&start=0&length={text.Length + 1}", "This passage is not in the saved text");
        await AssertStatusAsync(HttpStatusCode.BadRequest, $"{url}&start=999999999999&length=1", "This link is not valid");
        await AssertStatusAsync(HttpStatusCode.BadRequest, $"{url}&start=-1&length=1", "This link is not valid");
        await AssertStatusAsync(HttpStatusCode.BadRequest, $"{url}&start=0&length=0", "This link is not valid");
    }

    // Wrong links and missing records must say so plainly with the right status, never show an empty success page.
    [Fact]
    public async Task InvalidIdsAndMissingRecordsReturnTheirOwnStates()
    {
        var missing = new string('f', 64);
        var invalid = new string('F', 64);

        await AssertStatusAsync(HttpStatusCode.NotFound, "/Documents/Extraction?extractionId=" + missing, "Saved text not found");
        await AssertStatusAsync(HttpStatusCode.BadRequest, "/Documents/Extraction?extractionId=" + invalid, "This link is not valid");
        await AssertStatusAsync(HttpStatusCode.BadRequest, "/Documents/Extraction", "This link is not valid");
        await AssertStatusAsync(HttpStatusCode.NotFound, $"/Documents/Citation?extractionId={missing}&start=0&length=1", "Saved text not found");
        await AssertStatusAsync(HttpStatusCode.BadRequest, $"/Documents/Citation?extractionId={invalid}&start=0&length=1", "This link is not valid");
        await AssertStatusAsync(HttpStatusCode.NotFound, "/Documents/Comparison?comparisonId=" + missing, "Comparison not found");
        await AssertStatusAsync(HttpStatusCode.BadRequest, "/Documents/Comparison?comparisonId=" + invalid, "This link is not valid");
        await AssertStatusAsync(HttpStatusCode.BadRequest, "/Documents/History?sourceId=source", "This link is not valid");
    }

    // A failed check is a gap in coverage; history must show it beside the saved texts instead of hiding it.
    [Fact]
    public async Task HistoryListsFailedChecksAsCoverageGapsAndLinksEachSavedText()
    {
        var before = await host.SaveExtractionAsync("Deadline October 15.\nEligibility unchanged.");
        var after = await host.SaveExtractionAsync("Deadline October 30.\nEligibility unchanged.");
        var request = Request();
        var failed = Receipt(request) with
        {
            Outcome = CollectionOutcome.Failed,
            Capture = null,
            FailureCode = CollectionFailureCode.HttpError,
            Response = new HttpResponseMetadata { StatusCode = 503, ContentEncodings = [] }
        };
        await host.Attempts.ImportAtomicallyAsync(CollectionAttemptImporter.CreateImport("failed-check", request, failed), default);

        var page = await host.GetDocumentAsync("/Documents/History?sourceId=source&requestedUrl=" + Uri.EscapeDataString(request.Url));

        var rows = page.QuerySelectorAll("table tbody tr");
        Assert.Equal(3, rows.Length);
        var failedRow = Assert.Single(rows, row => row.QuerySelector(".badge")!.TextContent == "Could not save");
        Assert.Contains("HTTP 503", failedRow.TextContent, StringComparison.Ordinal);
        Assert.Contains("coverage gap, not evidence of no activity", failedRow.TextContent, StringComparison.Ordinal);
        var links = page.QuerySelectorAll("a").Select(link => link.GetAttribute("href")).ToArray();
        Assert.Contains("/Documents/Extraction?extractionId=" + before.ExtractionId, links);
        Assert.Contains("/Documents/Extraction?extractionId=" + after.ExtractionId, links);
        Assert.Contains("parser-v1", page.QuerySelector(".tech")!.TextContent, StringComparison.Ordinal);
    }

    // Only a complete comparison with changed text may start a draft; other states must explain why there is nothing to review.
    [Fact]
    public async Task ComparisonShowsBothVersionsAndExplainsStatesThatCannotStartADraft()
    {
        var before = await host.SaveExtractionAsync("Deadline October 15.\nEligibility unchanged.");
        var after = await host.SaveExtractionAsync("Deadline October 30.\nEligibility unchanged.");
        var changed = await host.Comparisons.SaveAsync(DocumentComparison.Create(before, after), default);

        var page = await host.GetDocumentAsync("/Documents/Comparison?comparisonId=" + changed.ComparisonId);

        var view = page.QuerySelector(".comparison")!.TextContent;
        Assert.Contains("Deadline October 15.", view, StringComparison.Ordinal);
        Assert.Contains("Deadline October 30.", view, StringComparison.Ordinal);
        Assert.Contains(page.QuerySelectorAll("a"), link => link.GetAttribute("href")!.StartsWith("/Documents/Citation?", StringComparison.Ordinal));
        Assert.Equal(2, page.QuerySelectorAll(".page-header-meta time").Length);
        Assert.NotNull(StartDraftForm(page));

        var upgraded = await host.Extractions.SaveAsync(new DocumentExtraction(after.SourceAttempt, "parser-v2", "normalization-v1", after.Text), default);
        var incompatible = await host.Comparisons.SaveAsync(DocumentComparison.Create(before, upgraded), default);
        var incompatiblePage = await host.GetDocumentAsync("/Documents/Comparison?comparisonId=" + incompatible.ComparisonId);
        Assert.Contains("incompatible", incompatiblePage.QuerySelector(".notice")!.TextContent, StringComparison.Ordinal);
        Assert.Null(StartDraftForm(incompatiblePage));

        var longText = await host.SaveExtractionAsync(string.Join('\n', Enumerable.Repeat("line", 2002)));
        var limited = await host.Comparisons.SaveAsync(DocumentComparison.Create(before, longText), default);
        var limitedPage = await host.GetDocumentAsync("/Documents/Comparison?comparisonId=" + limited.ComparisonId);
        Assert.Contains("limit", limitedPage.QuerySelector(".notice")!.TextContent, StringComparison.Ordinal);
        Assert.Null(StartDraftForm(limitedPage));
    }

    // Drafting changes review state, so the form needs a valid antiforgery token and a replay must open the same draft.
    [Fact]
    public async Task StartDraftRequiresAntiforgeryAndReplaysToTheSameDraft()
    {
        var before = await host.SaveExtractionAsync("Earlier wording.\n");
        var after = await host.SaveExtractionAsync("<img src=x onerror=alert(1)>\n");
        var comparison = await host.Comparisons.SaveAsync(DocumentComparison.Create(before, after), default);
        host.Client.DefaultRequestHeaders.Remove("Cookie");
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));
        var url = "/Documents/Comparison?comparisonId=" + comparison.ComparisonId;
        var page = await host.GetDocumentAsync(url);
        Assert.DoesNotContain("<img src=x", page.Body!.InnerHtml, StringComparison.Ordinal);
        var form = StartDraftForm(page)!;

        using var forged = await host.Client.PostAsync(url, new FormUrlEncodedContent(
            ReviewWorkspaceHost.Fields(form).Where(field => field.Key != "__RequestVerificationToken")));
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        using var created = await host.Client.PostAsync(url, ReviewWorkspaceHost.Form(form));
        using var replay = await host.Client.PostAsync(url, ReviewWorkspaceHost.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Matches("^https://review.example.test/Review/[0-9a-f]{32}$", created.Headers.Location!.ToString());
        Assert.Equal(created.Headers.Location, replay.Headers.Location);
    }

    // Corrupt storage must fail closed without leaking provider details or the corrupted text.
    [Fact]
    public async Task CorruptEvidenceFailsWithoutDisclosingProviderDetails()
    {
        var extraction = await host.SaveExtractionAsync("original text");
        await using var connection = new NpgsqlConnection(host.ConnectionString);
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand("UPDATE document_extractions SET text = 'corrupted' WHERE extraction_id = @id", connection))
        {
            command.Parameters.AddWithValue("id", extraction.ExtractionId);
            await command.ExecuteNonQueryAsync();
        }

        var html = await AssertStatusAsync(HttpStatusCode.ServiceUnavailable, "/Documents/Extraction?extractionId=" + extraction.ExtractionId, "Evidence is unavailable");
        Assert.DoesNotContain(host.ConnectionString, html, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", html, StringComparison.Ordinal);
        Assert.DoesNotContain("corrupted", html, StringComparison.Ordinal);

        await using (var command = new NpgsqlCommand("UPDATE collection_attempts SET final_url = chr(9) WHERE attempt_id = @id", connection))
        {
            command.Parameters.AddWithValue("id", extraction.SourceAttempt.AttemptId);
            await command.ExecuteNonQueryAsync();
        }
        var history = await AssertStatusAsync(HttpStatusCode.ServiceUnavailable,
            "/Documents/History?sourceId=source&requestedUrl=" + Uri.EscapeDataString(extraction.SourceAttempt.RequestedUrl), "Evidence is unavailable");
        Assert.DoesNotContain("Exception", history, StringComparison.Ordinal);
    }

    private static IElement? StartDraftForm(IDocument page) =>
        page.QuerySelectorAll("form").SingleOrDefault(form => form.QuerySelector("input[name='IdempotencyKey']") is not null);

    private async Task<string> AssertStatusAsync(HttpStatusCode expected, string url, string title)
    {
        using var response = await host.Client.GetAsync(url);
        Assert.Equal(expected, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var document = await new HtmlParser().ParseDocumentAsync(html);
        Assert.Equal(title, document.QuerySelector(".notice strong")!.TextContent);
        Assert.NotNull(document.QuerySelector(".empty"));
        return html;
    }

    private async Task<(long Extractions, long Comparisons)> CountEvidenceAsync()
    {
        await using var connection = new NpgsqlConnection(host.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT (SELECT count(*) FROM document_extractions), (SELECT count(*) FROM document_comparisons)", connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt64(0), reader.GetInt64(1));
    }
}
