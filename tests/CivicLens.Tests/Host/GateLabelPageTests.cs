using System.Net;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Statements;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;
using static CivicLens.Tests.Host.ReviewWorkspaceHost;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class GateLabelPageTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Owner = "auth0|owner";
    private const string Reviewer = "auth0|friend";
    private const string RentIssue = StatementGateFixtures.RentIssue;
    private readonly ReviewWorkspaceHost host = new(postgres)
    {
        Issues = StatementGateFixtures.Configuration(),
        OfficialId = StatementDraftFixtures.Warren,
        OfficialName = "Elizabeth Warren"
    };
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "civic-gate-label-pages-" + Guid.NewGuid().ToString("N"));
    private PostgresGateLabelStore labels = null!;
    private StatementCandidateSeeder seeder = null!;
    private int seeded;

    public async Task InitializeAsync()
    {
        await host.StartAsync();
        var factory = AnalysisDbContextFactory.Create(host.ConnectionString);
        labels = new PostgresGateLabelStore(factory);
        Directory.CreateDirectory(captureRoot);
        seeder = new StatementCandidateSeeder(host.Attempts, new PostgresDocumentExtractionStore(factory), host.ConnectionString, captureRoot);
    }

    public async Task DisposeAsync()
    {
        await host.StopAsync();
        Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task TheListShowsHeldOutReleasesFirstAndLinksEachReleaseToItsLabelForm()
    {
        var open = await SeedAsync(heldOut: false);
        var heldOut = await SeedAsync(heldOut: true);
        SignIn(Owner);

        var page = await host.GetDocumentAsync("/Review/GateLabels");

        var rows = page.QuerySelectorAll("[aria-labelledby='unlabeled'] tbody tr").ToArray();
        var heldFlags = rows.Select(row => row.TextContent.Contains("Held out", StringComparison.Ordinal)).ToArray();
        Assert.Equal(heldFlags.OrderByDescending(flag => flag), heldFlags);
        var links = rows.Select(row => row.QuerySelector("a.cell-title")!.GetAttribute("href")).ToArray();
        Assert.Contains($"/Review/GateLabels/{heldOut}", links);
        Assert.Contains($"/Review/GateLabels/{open}", links);
    }

    [Theory]
    [InlineData("/Review/GateLabels")]
    [InlineData("/Review/GateLabels/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task ReviewersAreSentToAccessDeniedSoOnlyTheOwnerFeedsTheBenchmark(string url)
    {
        SignIn(Reviewer);

        using var denied = await host.Client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Contains("access-denied", denied.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHeldOutReleaseShowsItsTextAndTheHeldOutNoticeWithNothingPrefilled()
    {
        var candidate = await SeedAsync(heldOut: true);
        SignIn(Owner);

        var page = await host.GetDocumentAsync($"/Review/GateLabels/{candidate}");

        Assert.Contains("held out", page.QuerySelector(".notice")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("rent cap", page.QuerySelector("[aria-labelledby='release-text']")!.TextContent, StringComparison.Ordinal);
        Assert.Empty(page.QuerySelectorAll("#label-form input[checked]"));
        Assert.Equal(["Input.IssueIds", "Input.IssueIds", "Input.IssueIds"], IssueBoxes(page).Select(box => box.GetAttribute("name")));
    }

    [Fact]
    public async Task AModelRowOnAHeldOutReleaseNeverPrefillsTheFormSoTheTestSetStaysBlind()
    {
        var candidate = await SeedAsync(heldOut: true);
        var extraction = (await labels.GetAsync(candidate, default))!.ExtractionId;
        var run = new string('c', 32);
        await GateLabelRuns.InsertAsync(host.ConnectionString, candidate, run);
        var json = $$"""{"issueIds":["{{RentIssue}}"],"namesBillOrVote":true}""";
        await using (var connection = new NpgsqlConnection(host.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"""
                INSERT INTO statement_gate_labels (label_id, candidate_id, extraction_id, author_subject, author_kind, run_id, label_json, created_at_utc_ticks)
                VALUES ('{new string('d', 32)}', '{candidate}', '{extraction}', 'civic-lens:analysis', 'Analysis', '{run}', '{json}', 0)
                """, connection);
            await command.ExecuteNonQueryAsync();
        }
        SignIn(Owner);

        var page = await host.GetDocumentAsync($"/Review/GateLabels/{candidate}");

        Assert.Empty(page.QuerySelectorAll("#label-form input[checked]"));
        Assert.DoesNotContain("A model suggested", page.Body!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReleaseWithAModelLabelPrefillsItAndSavingItAddsAHumanLabelOfRecord()
    {
        var candidate = await SeedAsync(heldOut: false);
        var extraction = (await labels.GetAsync(candidate, default))!.ExtractionId;
        await GateLabelRuns.InsertAsync(host.ConnectionString, candidate, new string('c', 32));
        await labels.AddAsync(new(candidate, extraction, ReviewAuthor.AnalysisSubject, ReviewAuthorKind.Analysis, new string('c', 32),
            new GateLabel([RentIssue], true), DateTimeOffset.UtcNow), default);
        SignIn(Owner);
        var url = $"/Review/GateLabels/{candidate}";
        var page = await host.GetDocumentAsync(url);
        Assert.Contains("A model suggested", page.QuerySelector(".notice")!.TextContent, StringComparison.Ordinal);
        Assert.Equal([RentIssue], IssueBoxes(page).Where(box => box.HasAttribute("checked")).Select(box => box.GetAttribute("value")));
        Assert.Equal("yes", page.QuerySelector("input[name='Input.NamesBillOrVote'][checked]")!.GetAttribute("value"));

        using var saved = await host.Client.PostAsync(url, Form(page.QuerySelector("#label-form")!));

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var task = (await labels.GetAsync(candidate, default))!;
        Assert.Equal((RentIssue, true), (Assert.Single(task.HumanLabel!.IssueIds), task.HumanLabel.NamesBillOrVote));
        Assert.DoesNotContain(candidate, (await labels.ListUnlabeledAsync(100, default)).Items.Select(item => item.CandidateId));
    }

    [Fact]
    public async Task ACorrectionAddsANewerHumanLabelThatReplacesTheEarlierOneAsTheLabelOfRecord()
    {
        var candidate = await SeedAsync(heldOut: true);
        SignIn(Owner);
        var url = $"/Review/GateLabels/{candidate}";
        var fields = await FieldsAsync(candidate);
        fields.Add(new("Input.IssueIds", RentIssue));
        using var first = await host.Client.PostAsync(url, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        var again = await FieldsAsync(candidate);
        Assert.Equal(RentIssue, Assert.Single(again, field => field.Key == "Input.IssueIds").Value);
        Set(again, "Input.NamesBillOrVote", "yes");
        again.RemoveAll(field => field.Key == "Input.IssueIds");

        using var second = await host.Client.PostAsync(url, new FormUrlEncodedContent(again));

        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        var label = (await labels.GetAsync(candidate, default))!.HumanLabel!;
        Assert.Equal((0, true), (label.IssueIds.Length, label.NamesBillOrVote));
    }

    [Fact]
    public async Task AnIssueOutsideTheIssueFileOrAMissingYesOrNoIsRefusedAndWritesNothing()
    {
        var candidate = await SeedAsync(heldOut: false);
        SignIn(Owner);
        var url = $"/Review/GateLabels/{candidate}";
        var unknown = await FieldsAsync(candidate);
        unknown.Add(new("Input.IssueIds", "invented-issue"));
        var undecided = await FieldsAsync(candidate);
        undecided.RemoveAll(field => field.Key == "Input.NamesBillOrVote");

        using var badIssue = await host.Client.PostAsync(url, new FormUrlEncodedContent(unknown));
        using var noAnswer = await host.Client.PostAsync(url, new FormUrlEncodedContent(undecided));

        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (badIssue.StatusCode, noAnswer.StatusCode));
        Assert.Null((await labels.GetAsync(candidate, default))!.HumanLabel);
    }

    [Fact]
    public async Task AnUnknownOrMalformedReleaseIsNotFound()
    {
        SignIn(Owner);

        foreach (var id in new[] { new string('a', 64), "not-a-candidate" })
        {
            using var missing = await host.Client.GetAsync($"/Review/GateLabels/{id}");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }

    private static IEnumerable<AngleSharp.Dom.IElement> IssueBoxes(AngleSharp.Dom.IDocument page) =>
        page.QuerySelectorAll("#label-form input[name='Input.IssueIds']");

    private async Task<List<KeyValuePair<string, string>>> FieldsAsync(string candidate)
    {
        var page = await host.GetDocumentAsync($"/Review/GateLabels/{candidate}");
        var fields = Fields(page.QuerySelector("#label-form")!);
        if (!fields.Any(field => field.Key == "Input.NamesBillOrVote")) fields.Add(new("Input.NamesBillOrVote", "no"));
        return fields;
    }

    private async Task<string> SeedAsync(bool heldOut)
    {
        while (true)
        {
            var number = ++seeded;
            var candidate = await seeder.SeedAsync($"The office announced a rent cap for tenants. Release {number}.", $"https://example.test/pages/{number}");
            if (GateLabelSplit.IsHeldOut(candidate) == heldOut) return candidate;
        }
    }

    private void SignIn(string subject) => host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie(subject));
}
