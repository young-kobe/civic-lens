using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.Application.Review;
using CivicLens.Application.Statements;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Statements;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using static CivicLens.Tests.Fixtures.StatementDraftFixtures;
using static CivicLens.Tests.Host.ReviewWorkspaceHost;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class StatementReviewWorkspaceTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Owner = "auth0|owner";
    private const string Reviewer = "auth0|friend";
    private const string Headline = "Warren announces a rent cap";
    private const string Quote = "a rent cap for tenants";
    private readonly ReviewWorkspaceHost host = new(postgres)
    {
        Issues = StatementGateFixtures.Configuration(),
        OfficialId = StatementDraftFixtures.Warren,
        OfficialName = "Elizabeth Warren"
    };
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "civic-statement-pages-" + Guid.NewGuid().ToString("N"));
    private PostgresStatementGateStore gates = null!;
    private PostgresStatementDraftStore drafts = null!;
    private PostgresStatementReviewStore reviews = null!;
    private StatementCandidateSeeder seeder = null!;
    private int candidateCount;

    public async Task InitializeAsync()
    {
        await host.StartAsync();
        var factory = AnalysisDbContextFactory.Create(host.ConnectionString);
        gates = new PostgresStatementGateStore(factory);
        drafts = new PostgresStatementDraftStore(factory);
        reviews = new PostgresStatementReviewStore(factory);
        Directory.CreateDirectory(captureRoot);
        seeder = new StatementCandidateSeeder(host.Attempts, new PostgresDocumentExtractionStore(factory), host.ConnectionString, captureRoot);
    }

    public async Task DisposeAsync()
    {
        await host.StopAsync();
        Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task TheListShowsTheAiDraftWithItsStatusAndOffersADraftForAnArticleTheGateSkippedAsync()
    {
        var drafted = await AiDraftedAsync();
        var skipped = await SkippedAsync();
        SignIn(Owner);

        var page = await host.GetDocumentAsync("/Review/Statements");

        var link = page.QuerySelector("[aria-labelledby='drafts'] a.cell-title")!;
        Assert.Equal(($"/Review/Statements/{drafted}", Headline), (link.GetAttribute("href"), link.TextContent));
        Assert.Equal(["Awaiting review"], page.QuerySelectorAll("[aria-labelledby='drafts'] tbody .badge").Select(badge => badge.TextContent));
        var start = page.QuerySelector($"[aria-labelledby='not-in-review'] button[form='start-statement'][value='{skipped}']")!;
        Assert.Equal("Start draft", start.TextContent);
        Assert.Contains("no-tracked-issue", page.QuerySelector("[aria-labelledby='not-in-review'] tbody")!.TextContent, StringComparison.Ordinal);
        Assert.Equal("page", page.QuerySelector(".nav a[href='/Review/Statements']")!.GetAttribute("aria-current"));
        Assert.Null(page.QuerySelector(".nav a[href='/Review']")!.GetAttribute("aria-current"));
        var home = await host.GetDocumentAsync("/Review");
        Assert.NotNull(home.QuerySelector(".nav a[href='/Review/Statements']"));
    }

    [Fact]
    public async Task TheFilterTabsSplitDraftsByWhetherTheyNeedActionAsync()
    {
        await AiDraftedAsync();
        SignIn(Reviewer);

        var needsAction = await host.GetDocumentAsync("/Review/Statements?drafts=needs-action");
        var approved = await host.GetDocumentAsync("/Review/Statements?drafts=approved");

        Assert.Equal(["/Review/Statements#drafts", "/Review/Statements?drafts=needs-action#drafts", "/Review/Statements?drafts=approved#drafts"],
            needsAction.QuerySelectorAll("[aria-labelledby='drafts'] a.segmented-item").Select(tab => tab.GetAttribute("href")));
        Assert.Single(needsAction.QuerySelectorAll("[aria-labelledby='drafts'] a.cell-title"));
        Assert.Equal("No approved statements", approved.QuerySelector("[aria-labelledby='drafts'] .empty h3")!.TextContent);
    }

    [Fact]
    public async Task TheDetailPageLabelsTheAiRevisionAndRendersEachQuotationWithItsKindAndTheMarkedArticleAsync()
    {
        var candidate = await AiDraftedAsync();
        SignIn(Owner);

        var page = await host.GetDocumentAsync($"/Review/Statements/{candidate}");

        Assert.Contains("The AI drafter wrote this revision in analysis run", page.Body!.TextContent, StringComparison.Ordinal);
        var saved = page.QuerySelector("#saved-approval-target")!.TextContent;
        Assert.Contains(Headline, saved, StringComparison.Ordinal);
        Assert.Contains(Quote, saved, StringComparison.Ordinal);
        Assert.Contains("Release text", saved, StringComparison.Ordinal);
        Assert.Contains("Elizabeth Warren", saved, StringComparison.Ordinal);
        Assert.Contains("2026-10-03", saved, StringComparison.Ordinal);
        Assert.Contains("AI draft written", page.QuerySelector("[aria-labelledby='history']")!.TextContent, StringComparison.Ordinal);
        Assert.Contains(Quote, page.QuerySelectorAll("[aria-labelledby='evidence'] mark").Select(mark => mark.TextContent));
        Assert.Contains("Unverified statement date", page.QuerySelector("#unconfirmed-statement-date")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASaveMakesRevisionTwoByThePersonKeepsTheCheckedAiDateAndRedirectsAsync()
    {
        var candidate = await AiDraftedAsync();
        SignIn(Owner);
        var url = $"/Review/Statements/{candidate}";
        var fields = await SaveFieldsAsync(url);
        Set(fields, "Input.Headline", "Reviewer headline");
        fields.Add(new("Input.StatementDateChecked", "true"));

        using var saved = await host.Client.PostAsync(url, new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Equal(url + "?status=saved", PathOf(saved));
        var revision = (await reviews.GetAsync(candidate, default))!.Review.CurrentRevision!;
        Assert.Equal((2, Owner, "Reviewer headline"), (revision.RevisionNumber, revision.AuthorSubject, revision.Headline));
        Assert.Equal(new DateOnly(2026, 10, 3), revision.StatementDate);
        Assert.Equal([StatementDraftFixtures.Warren], revision.SpeakerOfficialIds.AsEnumerable());
        Assert.Single(revision.Quotations);
        var page = await host.GetDocumentAsync(url + "?status=saved");
        Assert.DoesNotContain("The AI drafter wrote this revision", page.Body!.TextContent, StringComparison.Ordinal);
        Assert.Contains("The new revision is saved", page.Body.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASaveWithoutTheDateTickDropsTheAiDateAsync()
    {
        var candidate = await AiDraftedAsync();
        SignIn(Owner);
        var url = $"/Review/Statements/{candidate}";

        using var saved = await host.Client.PostAsync(url, new FormUrlEncodedContent(await SaveFieldsAsync(url)));

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var revision = (await reviews.GetAsync(candidate, default))!.Review.CurrentRevision!;
        Assert.Null(revision.StatementDate);
        Assert.Null(revision.StatementDateEvidence);
    }

    [Fact]
    public async Task AMixedSelectionKeepsEachRecordKindThroughTheSummaryTheConfirmFormAndTheReleaseAsync()
    {
        var statement = await AiDraftedAsync();
        SignIn(Owner);
        var statementUrl = $"/Review/Statements/{statement}";
        var fields = await SaveFieldsAsync(statementUrl);
        fields.Add(new("Input.StatementDateChecked", "true"));
        using var saved = await host.Client.PostAsync(statementUrl, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        using var approved = await DecideAsync(statementUrl, "Approve");
        Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);
        var change = await ApprovedDocumentChangeAsync();

        var home = await host.GetDocumentAsync("/Review");
        var offered = home.QuerySelectorAll("input[name=publish]").Select(box => box.GetAttribute("value")).Order().ToList();
        Assert.Equal([$"documentChange:{change}", $"statement:{statement}"], offered);
        var kinds = home.QuerySelectorAll("[aria-labelledby='publish'] tbody tr").Select(row => row.QuerySelectorAll("td")[2].TextContent);
        Assert.Equal(["Document change", "Statement"], kinds.Order());
        var links = home.QuerySelectorAll("[aria-labelledby='publish'] a.cell-title").Select(link => link.GetAttribute("href"));
        Assert.Contains($"/Review/Statements/{statement}", links);
        Assert.Contains($"/Review/{change}", links);

        var summary = await host.GetDocumentAsync("/Review?" + string.Join('&', offered.Select(value => "publish=" + Uri.EscapeDataString(value!))));
        var confirm = Fields(summary.QuerySelector("#publish-confirm")!);
        Assert.Equal(offered, confirm.Where(field => field.Key == "records").Select(field => field.Value).Order());
        using var published = await host.Client.PostAsync("/Review", new FormUrlEncodedContent(confirm));
        Assert.Equal(HttpStatusCode.Redirect, published.StatusCode);
        Assert.Equal("/Review?status=published&release=1", PathOf(published).Split('#')[0]);
        var current = Path.Combine(host.ReleaseDirectory, "current");
        Assert.True(File.Exists(Path.Combine(current, PublicationProtocol.RecordPagePath(statement))));
        Assert.True(File.Exists(Path.Combine(current, PublicationProtocol.RecordPagePath(change))));
    }

    [Fact]
    public async Task AStaleSaveReturnsConflictKeepsTheTypedWordingAndShowsBothVersionsAsync()
    {
        var candidate = await AiDraftedAsync();
        SignIn(Owner);
        var url = $"/Review/Statements/{candidate}";
        var fields = await SaveFieldsAsync(url);
        using var first = await host.Client.PostAsync(url, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);

        Set(fields, "Input.Headline", "Recovered unsaved headline");
        Set(fields, "idempotencyKey", Guid.NewGuid().ToString("N"));
        using var stale = await host.Client.PostAsync(url, new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var conflict = await ParseAsync(stale);
        Assert.Equal("Recovered unsaved headline", conflict.QuerySelector("#Input_Headline")!.GetAttribute("value"));
        Assert.Equal("edit", conflict.QuerySelector("[data-tabset]")!.GetAttribute("data-selected"));
        Assert.Contains("Recovered unsaved headline", conflict.QuerySelector("[aria-labelledby='conflict-compare']")!.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Recovered unsaved headline", conflict.QuerySelector("#saved-approval-target")!.TextContent, StringComparison.Ordinal);
        Assert.True(conflict.QuerySelector("#decision-controls")!.HasAttribute("disabled"));
        Assert.Equal(2, (await reviews.GetAsync(candidate, default))!.Review.CurrentRevision!.RevisionNumber);
    }

    [Fact]
    public async Task AnInvalidSaveReturnsBadRequestAndAnUnofferedPassageIsRefusedAsync()
    {
        var candidate = await AiDraftedAsync();
        SignIn(Owner);
        var url = $"/Review/Statements/{candidate}";
        var fields = await SaveFieldsAsync(url);
        Set(fields, "Input.QuotationSelection", "0:1");
        fields.Add(new("Input.QuotationSelection", "999999:5"));

        using var refused = await host.Client.PostAsync(url, new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(1, (await reviews.GetAsync(candidate, default))!.Review.CurrentRevision!.RevisionNumber);
    }

    [Fact]
    public async Task AnAiDateBlocksApprovalUntilAPersonSavesItCheckedAsync()
    {
        var candidate = await AiDraftedAsync();
        SignIn(Owner);
        var url = $"/Review/Statements/{candidate}";

        using var blocked = await DecideAsync(url, "Approve");
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);

        var fields = await SaveFieldsAsync(url);
        fields.Add(new("Input.StatementDateChecked", "true"));
        using var saved = await host.Client.PostAsync(url, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        using var approved = await DecideAsync(url, "Approve");

        Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);
        Assert.Equal(url + "?status=decided", PathOf(approved));
        Assert.Equal(ReviewStatus.Approved, (await reviews.GetAsync(candidate, default))!.Review.CurrentStatus);
        var page = await host.GetDocumentAsync(url);
        Assert.Equal("Approved, private", page.QuerySelector(".page-header .badge")!.TextContent);
        Assert.NotNull(page.QuerySelector("#decision-form button[value='WithdrawApproval']"));
    }

    [Fact]
    public async Task RequestingChangesNeedsANoteAndTheReasonShowsInTheHistoryAsync()
    {
        var candidate = await AiDraftedAsync();
        SignIn(Reviewer);
        var url = $"/Review/Statements/{candidate}";

        using var withoutNote = await DecideAsync(url, "RequestChanges");
        Assert.Equal(HttpStatusCode.BadRequest, withoutNote.StatusCode);
        using var withNote = await DecideAsync(url, "RequestChanges", "Name the housing bill.");

        Assert.Equal(HttpStatusCode.Redirect, withNote.StatusCode);
        var page = await host.GetDocumentAsync(url);
        Assert.Contains("Name the housing bill.", page.QuerySelector("[aria-labelledby='history'] tbody")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Approval is blocked", page.Body!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartingADraftFromTheListCreatesRevisionOneAndOpensTheDetailPageAsync()
    {
        await AiDraftedAsync();
        var skipped = await SkippedAsync();
        SignIn(Owner);
        var list = await host.GetDocumentAsync("/Review/Statements");
        var fields = Fields(list.QuerySelector("#start-statement")!);
        fields.Add(new("candidateId", skipped));

        using var started = await host.Client.PostAsync("/Review/Statements", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Redirect, started.StatusCode);
        Assert.Equal($"/Review/Statements/{skipped}?status=started", PathOf(started));
        var revision = (await reviews.GetAsync(skipped, default))!.Review.CurrentRevision!;
        Assert.Equal((1, Owner), (revision.RevisionNumber, revision.AuthorSubject));
        var page = await host.GetDocumentAsync($"/Review/Statements/{skipped}");
        Assert.Contains("Draft started", page.QuerySelector("[aria-labelledby='history']")!.TextContent, StringComparison.Ordinal);
        Assert.Null(page.QuerySelector("[aria-labelledby='conflict-compare']"));
        Assert.Null(page.QuerySelector("#unconfirmed-statement-date"));
    }

    [Fact]
    public async Task ANotDraftedStatementShowsItsEvidenceAndStartsADraftFromTheDetailPageAsync()
    {
        var skipped = await SkippedAsync();
        SignIn(Owner);
        var url = $"/Review/Statements/{skipped}";

        var page = await host.GetDocumentAsync(url);

        Assert.Equal("Not drafted", page.QuerySelector(".page-header .badge")!.TextContent);
        Assert.Null(page.QuerySelector("#draft-form"));
        Assert.Contains(Quote, page.QuerySelector("[aria-labelledby='evidence'] .doc-text")!.TextContent, StringComparison.Ordinal);
        var start = page.QuerySelector("button[form='start-draft']")!;
        Assert.Equal("Start draft", start.TextContent);

        using var started = await host.Client.PostAsync(url, new FormUrlEncodedContent(Fields(page.QuerySelector("#start-draft")!)));

        Assert.Equal(HttpStatusCode.Redirect, started.StatusCode);
        Assert.Equal(url + "?status=started", PathOf(started));
        Assert.Equal(1, (await reviews.GetAsync(skipped, default))!.Review.CurrentRevision!.RevisionNumber);
    }

    [Fact]
    public async Task PostsWithoutTheAntiforgeryTokenAreRefusedAndWriteNothingAsync()
    {
        var candidate = await AiDraftedAsync();
        SignIn(Owner);
        var url = $"/Review/Statements/{candidate}";
        var fields = (await SaveFieldsAsync(url)).Where(field => field.Key != "__RequestVerificationToken");

        using var forged = await host.Client.PostAsync(url, new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.Equal(1, (await reviews.GetAsync(candidate, default))!.Review.CurrentRevision!.RevisionNumber);
    }

    [Fact]
    public async Task AnUnknownOrMalformedCandidateIsNotFoundAndItsPostsAreNotFoundAsync()
    {
        var candidate = await AiDraftedAsync();
        SignIn(Owner);
        var fields = await SaveFieldsAsync($"/Review/Statements/{candidate}");

        foreach (var id in new[] { new string('a', 64), "not-a-candidate" })
        {
            using var missing = await host.Client.GetAsync($"/Review/Statements/{id}");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            using var missingSave = await host.Client.PostAsync($"/Review/Statements/{id}", new FormUrlEncodedContent(fields));
            Assert.Equal(HttpStatusCode.NotFound, missingSave.StatusCode);
        }
    }

    private async Task<string> AiDraftedAsync()
    {
        var candidate = await seeder.SeedAsync($"{Article} Release {++candidateCount}.", $"https://example.test/pages/{candidateCount}");
        await StatementAiDrafting.PassGateAsync(gates);
        await StatementAiDrafting.DraftAsync(drafts, Output(date: ("2026-10-03", "October 3, 2026")));
        return candidate;
    }

    private async Task<string> ApprovedDocumentChangeAsync()
    {
        var before = await host.SaveExtractionAsync("Fee: 10 dollars.\n");
        var after = await host.SaveExtractionAsync("Fee: 12 dollars.\n");
        var comparison = await host.Comparisons.CompareAndSaveAsync(before.ExtractionId, after.ExtractionId, default);
        var store = PostgresDocumentChangeReviewStore.FromConnectionString(host.ConnectionString);
        var catalog = new ReviewCatalog([StatementGateFixtures.RentIssue], [StatementDraftFixtures.Warren]);
        var actor = new ReviewActor(Owner, ReviewRole.Owner);
        var draft = await new CreateDocumentChangeDraft(store).ExecuteAsync(actor, new(comparison.ComparisonId, "create"));
        var revision = await new SaveDocumentChangeDraft(store, catalog).ExecuteAsync(actor,
            new(draft.DraftId, 1, "Fee rose to 12 dollars", "The fee increased.", null, "Only this page was compared.",
                "City Council", null, null, [StatementDraftFixtures.Warren], [StatementGateFixtures.RentIssue], draft.Citations, "save"));
        await new DecideDocumentChangeReview(store, catalog).ExecuteAsync(actor,
            new(revision.DraftId, 2, 0, ReviewDecisionKind.Approve, null, [], "approve"));
        return revision.DraftId;
    }

    private async Task<string> SkippedAsync()
    {
        var candidate = await seeder.SeedAsync($"The office announced a rent cap for tenants. Release {++candidateCount}.", $"https://example.test/pages/{candidateCount}");
        await StatementAiDrafting.SkipGateAsync(gates);
        return candidate;
    }

    private async Task<List<KeyValuePair<string, string>>> SaveFieldsAsync(string url)
    {
        var page = await host.GetDocumentAsync(url);
        var fields = Fields(page.QuerySelector("#draft-form")!);
        Assert.Contains(fields, field => field.Key == "Input.QuotationSelection");
        return fields;
    }

    private async Task<HttpResponseMessage> DecideAsync(string url, string kind, string? note = null)
    {
        var page = await host.GetDocumentAsync(url);
        var fields = Fields(page.QuerySelector("#decision-form")!);
        Set(fields, "kind", kind);
        if (note is not null) Set(fields, "Decision.Note", note);
        return await host.Client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    private void SignIn(string subject) => host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie(subject));

    private static string PathOf(HttpResponseMessage response) => response.Headers.Location!.IsAbsoluteUri
        ? response.Headers.Location.PathAndQuery : response.Headers.Location.ToString();

    private static async Task<IDocument> ParseAsync(HttpResponseMessage response) =>
        await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());
}
