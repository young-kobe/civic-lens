using CivicLens.Host.Documents;
using Microsoft.Extensions.Logging;
using CivicLens.Application.Activity;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Health;
using CivicLens.Application.Paging;
using CivicLens.Application.Publication;
using CivicLens.Application.Reports;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Host.Collection;
using CivicLens.Host.Components.Layout;
using CivicLens.Host.Components.Ui;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace CivicLens.Host.Review;

public sealed partial class ReviewHome
{
    private const int PageSize = 10;
    private const int ReleaseHistoryLimit = 5;

    private static readonly IReadOnlyList<Crumb> Crumbs = [new("Review")];

    private readonly string renderKey = Guid.NewGuid().ToString("N");
    private ReviewActor? actor;
    private CollectionConfiguration? configuration;
    private NewDocumentChangePage? changes;
    private IReadOnlyDictionary<string, DocumentChangeAnalysisRecord> analyses = new Dictionary<string, DocumentChangeAnalysisRecord>();
    private AnalysisStatus? analysisStatus;
    private SourceHealthReport? health;
    private PublicationReleaseList? releases;
    private ReviewQueuePage? queue;
    private IReadOnlyList<BlockedPublishedRecord>? blocked;
    private int openReports;
    private IReadOnlyList<FeedItem> activity = [];
    private string? error;
    private string? lookupMessage;
    private ApprovedRecordPage? results;
    private string? searchMessage;

    [Inject] private ReviewActorAccessor Actors { get; set; } = default!;
    [Inject] private ILogger<ReviewHome> Logger { get; set; } = default!;
    [Inject] private IServiceProvider Services { get; set; } = default!;
    [Inject] private ListEligibleDocumentComparisons ListChanges { get; set; } = default!;
    [Inject] private ListDocumentChangeAnalyses ListAnalyses { get; set; } = default!;
    [Inject] private GetDocumentChangeAnalysisStatus GetAnalysisStatus { get; set; } = default!;
    [Inject] private ListReviewQueue ListQueue { get; set; } = default!;
    [Inject] private SearchApprovedRecords SearchApproved { get; set; } = default!;
    [Inject] private CountOpenReaderReports CountReports { get; set; } = default!;
    [Inject] private GetRecentActivity GetActivity { get; set; } = default!;
    [Inject] private LookupDraft Lookup { get; set; } = default!;
    [Inject] private CreateDocumentChangeDraft CreateDraft { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [CascadingParameter] private HttpContext HttpContext { get; set; } = default!;
    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "queue")] private string? QueueCursor { get; set; }
    [SupplyParameterFromQuery(Name = "changes")] private string? ChangesCursor { get; set; }
    [SupplyParameterFromQuery(Name = "results")] private string? ResultsCursor { get; set; }
    [SupplyParameterFromQuery(Name = "status")] private string? Status { get; set; }
    [SupplyParameterFromQuery(Name = "find")] private string? Find { get; set; }
    [SupplyParameterFromQuery(Name = "q")] private string? Search { get; set; }

    [SupplyParameterFromForm(FormName = "start-draft", Name = "comparisonId")] private string? StartComparisonId { get; set; }
    [SupplyParameterFromForm(FormName = "start-draft", Name = "idempotencyKey")] private string? StartKey { get; set; }

    private string? StatusMessage => Status switch
    {
        "invalid" => "Choose a new change to start a draft.",
        "unavailable" => "The draft could not be started. Refresh the page and try again.",
        _ => null
    };

    protected override async Task OnInitializedAsync()
    {
        actor = Actors.GetActor((await AuthenticationState).User);
        if (HttpMethods.IsPost(HttpContext.Request.Method)) return;
        var cancellationToken = HttpContext.RequestAborted;
        if (Find is not null) lookupMessage = await FindDraftAsync(cancellationToken);
        if (!new[] { ChangesCursor, QueueCursor, ResultsCursor }.All(IsValidCursor))
        {
            error = "This page link is no longer valid. Go back to the first page of the list.";
            return;
        }
        if (Search is not null) searchMessage = await SearchAsync(cancellationToken);
        try
        {
            var collection = actor.Role == ReviewRole.Owner ? Services.GetService<CollectionWorkspace>() : null;
            changes = await ListChanges.ExecuteAsync(actor, ChangesCursor, PageSize, cancellationToken);
            analyses = await ListAnalyses.ExecuteAsync(actor,
                [.. changes.Items.Select(item => item.ComparisonId)], cancellationToken);
            analysisStatus = await GetAnalysisStatus.ExecuteAsync(actor, cancellationToken);
            queue = await ListQueue.ExecuteAsync(actor, QueueCursor, PageSize, cancellationToken);
            openReports = await CountReports.ExecuteAsync(actor, cancellationToken);
            if (collection is not null)
            {
                configuration = collection.GetConfiguration(actor);
                health = await collection.GetSourceHealthAsync(actor, cancellationToken);
            }
            if (actor.Role == ReviewRole.Owner) await LoadPublicationAsync(cancellationToken);
            activity = (await GetActivity.ExecuteAsync(actor, cancellationToken: cancellationToken)).Select(Describe).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            Logger.LogError(exception, "The review overview could not load.");
            error = "The review workspace could not load its records. Refresh the page to try again.";
        }
    }

    private async Task<string?> FindDraftAsync(CancellationToken cancellationToken)
    {
        DraftLookupHit? hit;
        try { hit = await Lookup.ExecuteAsync(actor!, Find, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (DraftLookupFormatException invalid) { return invalid.Message; }
        catch (Exception exception)
        {
            Logger.LogError(exception, "The draft lookup failed.");
            return "The lookup could not run. Refresh the page and try again.";
        }
        if (hit is null) return $"No draft for “{Find!.Trim()}”.";
        Navigation.NavigateTo(RecordRoutes.Editor(hit.Kind, hit.RecordId));
        return null;
    }

    private async Task<string?> SearchAsync(CancellationToken cancellationToken)
    {
        try { results = await SearchApproved.ExecuteAsync(actor!, Search, ResultsCursor, PageSize, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ArgumentException)
        {
            return $"Type 1 to {SearchApprovedRecords.MaximumQueryLength} characters to search.";
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "The approved record search failed.");
            return "The search could not run. Refresh the page and try again.";
        }
        return null;
    }

    private async Task StartDraftAsync()
    {
        var target = await CreateDraftAsync();
        Navigation.NavigateTo(target);
    }

    private async Task<string> CreateDraftAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        try
        {
            if (string.IsNullOrWhiteSpace(StartComparisonId) || string.IsNullOrWhiteSpace(StartKey))
                throw new ArgumentException("Choose a saved comparison to create a draft.");
            var revision = await CreateDraft.ExecuteAsync(actor!, new CreateDocumentChangeDraftRequest(
                StartComparisonId, $"{StartKey}:{StartComparisonId}"), cancellationToken);
            return DocumentRoutes.Editor(revision.DraftId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (DocumentChangeDraftExistsException existing) { return DocumentRoutes.Editor(existing.DraftId); }
        catch (ArgumentException) { return "/Review?status=invalid"; }
        catch (Exception exception)
        {
            Logger.LogError(exception, "A document change draft could not start.");
            return "/Review?status=unavailable";
        }
    }

    private FeedItem Describe(ActivityEvent item)
    {
        var who = ReviewAuthor.IsAnalysis(item.ActorSubject) ? "The AI drafter"
            : item.ActorSubject == actor!.Subject ? "You" : "A reviewer";
        var draft = string.IsNullOrWhiteSpace(item.Headline) ? "an untitled draft" : $"“{item.Headline}”";
        var source = item.SourceId is null ? "a source" : SourceLabels.Name(configuration, item.SourceId);
        return item.Kind switch
        {
            ActivityKind.DraftCreated => new($"{who} started {draft}.", item.OccurredAt, Tone.Neutral),
            ActivityKind.RevisionSaved => new($"{who} saved a revision of {draft}.", item.OccurredAt, Tone.Neutral),
            ActivityKind.DecisionRecorded => DescribeDecision(who, draft, item),
            ActivityKind.CheckStarted => new($"A check of {source} started.", item.OccurredAt, Tone.Live),
            ActivityKind.CheckFailed => new($"A check of {source} failed.", item.OccurredAt, Tone.Bad),
            _ => new($"A change was found in {source}.", item.OccurredAt, Tone.Ok)
        };
    }

    private static FeedItem DescribeDecision(string who, string draft, ActivityEvent item)
    {
        var kind = item.DecisionKind!.Value;
        var text = kind switch
        {
            ReviewDecisionKind.Approve => $"{who} approved {draft}.",
            ReviewDecisionKind.RequestChanges => $"{who} requested changes to {draft}.",
            _ => $"{who} withdrew approval of {draft}."
        };
        return new(text, item.OccurredAt, DraftStatusDisplay.Tone(kind));
    }

    private IReadOnlyList<QueueRow> StuckRows => [.. blocked?.Select(record => record.StuckText() is { } text ? QueueRow.Of(record, text) : null).OfType<QueueRow>() ?? []];

    private IReadOnlyList<QueueRow> QueueRows => [.. queue!.NewerCursor is null ? StuckRows : [], .. queue!.Items.Select(QueueRow.Of)];

    private static bool IsValidCursor(string? cursor)
    {
        try
        {
            PageCursor.RequireRank(PageCursor.Parse(cursor), ranked: false);
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    private string FirstQueueHref => PageHref("needs-a-person", null, ChangesCursor, ResultsCursor);

    private string? QueueHref(string? cursor) => cursor is null ? null : PageHref("needs-a-person", cursor, ChangesCursor, ResultsCursor);

    private string? ChangesHref(string? cursor) => cursor is null ? null : PageHref("new-changes", QueueCursor, cursor, ResultsCursor);

    private string? ResultsHref(string? cursor) => cursor is null ? null : PageHref("search-results", QueueCursor, ChangesCursor, cursor);

    private string PageHref(string fragment, string? queueCursor, string? changesCursor, string? resultsCursor)
    {
        var query = new List<string>();
        if (queueCursor is not null) query.Add("queue=" + Uri.EscapeDataString(queueCursor));
        if (changesCursor is not null) query.Add("changes=" + Uri.EscapeDataString(changesCursor));
        if (Search is not null) query.Add("q=" + Uri.EscapeDataString(Search));
        if (resultsCursor is not null) query.Add("results=" + Uri.EscapeDataString(resultsCursor));
        return "/Review" + (query.Count == 0 ? "" : "?" + string.Join('&', query)) + "#" + fragment;
    }

    private static string Count(int value, string singular, string plural) =>
        $"{value} {(value == 1 ? singular : plural)}";

    private async Task LoadPublicationAsync(CancellationToken cancellationToken)
    {
        if (Services.GetService<ListBlockedPublished>() is not { } listBlocked) return;
        blocked = await listBlocked.ExecuteAsync(actor!, cancellationToken);
        releases = await Services.GetRequiredService<ListPublicationReleases>().ExecuteAsync(actor!, ReleaseHistoryLimit, cancellationToken);
    }
}
