using CivicLens.Host.Documents;
using Microsoft.Extensions.Logging;
using CivicLens.Application.Activity;
using CivicLens.Application.Analysis;
using CivicLens.Application.Briefs;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Health;
using CivicLens.Application.Publication;
using CivicLens.Application.Relationships;
using CivicLens.Application.Reports;
using CivicLens.Application.Review;
using CivicLens.Application.Statements;
using CivicLens.Core.Review;
using CivicLens.Host.Collection;
using CivicLens.Host.Components.Layout;
using CivicLens.Host.Components.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace CivicLens.Host.Review;

public sealed partial class ReviewHome
{
    private const int PageSize = 10;

    private static readonly IReadOnlyList<Crumb> Crumbs = [new("Review")];

    private readonly string renderKey = Guid.NewGuid().ToString("N");
    private ReviewActor? actor;
    private CollectionConfiguration? configuration;
    private ReviewOverview? overview;
    private NewDocumentChangePage? changes;
    private IReadOnlyDictionary<string, DocumentChangeAnalysisRecord> analyses = new Dictionary<string, DocumentChangeAnalysisRecord>();
    private AnalysisStatus? analysisStatus;
    private DocumentChangeReviewPage? drafts;
    private SourceHealthReport? health;
    private StatementReviewCounts? statementCounts;
    private BriefReviewCounts? briefCounts;
    private RelationshipReviewCounts? relationshipCounts;
    private int openReports;
    private IReadOnlyList<FeedItem> activity = [];
    private string? error;
    private string? lookupMessage;

    [Inject] private ReviewActorAccessor Actors { get; set; } = default!;
    [Inject] private ILogger<ReviewHome> Logger { get; set; } = default!;
    [Inject] private IServiceProvider Services { get; set; } = default!;
    [Inject] private GetReviewOverview GetOverview { get; set; } = default!;
    [Inject] private ListEligibleDocumentComparisons ListChanges { get; set; } = default!;
    [Inject] private ListDocumentChangeAnalyses ListAnalyses { get; set; } = default!;
    [Inject] private GetDocumentChangeAnalysisStatus GetAnalysisStatus { get; set; } = default!;
    [Inject] private ListDocumentChangeReviews ListDrafts { get; set; } = default!;
    [Inject] private GetStatementReviewCounts GetStatementCounts { get; set; } = default!;
    [Inject] private GetBriefReviewCounts GetBriefCounts { get; set; } = default!;
    [Inject] private GetRelationshipReviewCounts GetRelationshipCounts { get; set; } = default!;
    [Inject] private CountOpenReaderReports CountReports { get; set; } = default!;
    [Inject] private GetRecentActivity GetActivity { get; set; } = default!;
    [Inject] private LookupDraft Lookup { get; set; } = default!;
    [Inject] private CreateDocumentChangeDraft CreateDraft { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [CascadingParameter] private HttpContext HttpContext { get; set; } = default!;
    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "changes")] private string? ChangesCursor { get; set; }
    [SupplyParameterFromQuery(Name = "drafts")] private string? DraftsFilterName { get; set; }
    [SupplyParameterFromQuery(Name = "draftsCursor")] private string? DraftsCursor { get; set; }
    [SupplyParameterFromQuery(Name = "status")] private string? Status { get; set; }
    [SupplyParameterFromQuery(Name = "find")] private string? Find { get; set; }

    [SupplyParameterFromForm(FormName = "start-draft", Name = "comparisonId")] private string? StartComparisonId { get; set; }
    [SupplyParameterFromForm(FormName = "start-draft", Name = "idempotencyKey")] private string? StartKey { get; set; }

    private DraftStatusFilter Filter => DraftFilterNames.Parse(DraftsFilterName);

    private string? StatusMessage => Status switch
    {
        "invalid" => "Choose a new change to start a draft.",
        "unavailable" => "The draft could not be started. Refresh the page and try again.",
        "published" => ReleaseNumber is { } number ? $"Release {number} is published." : "The release is published.",
        "publish-invalid" => "The selection could not be published. Choose drafts again.",
        "publish-note-required" => "Each replacement and each returned record needs a correction note. Choose the records again and write a note for each one.",
        "publish-correction-limit" => "A record in the selection has reached the most corrections with a note. Remove it from the selection.",
        "publish-conflict" => "Drafts or releases changed while you reviewed them. Check the list and try again.",
        "publish-forbidden" => "Only the owner can publish.",
        "publish-unavailable" => "The release could not be published. Refresh the page and try again.",
        _ => null
    };

    private Tone StatusTone => Status == "published" ? Tone.Ok : Tone.Bad;

    protected override async Task OnInitializedAsync()
    {
        actor = Actors.GetActor((await AuthenticationState).User);
        canPublish = actor.Role == ReviewRole.Owner && Services.GetService<PublishRelease>() is not null;
        publishingOff = actor.Role == ReviewRole.Owner && !canPublish;
        if (HttpMethods.IsPost(HttpContext.Request.Method)) return;
        var cancellationToken = HttpContext.RequestAborted;
        if (Find is not null) lookupMessage = await FindDraftAsync(cancellationToken);
        try
        {
            var collection = actor.Role == ReviewRole.Owner ? Services.GetService<CollectionWorkspace>() : null;
            overview = await GetOverview.ExecuteAsync(actor, cancellationToken);
            changes = await ListChanges.ExecuteAsync(actor, ChangesCursor, PageSize, cancellationToken);
            analyses = await ListAnalyses.ExecuteAsync(actor,
                [.. changes.Items.Select(item => item.ComparisonId)], cancellationToken);
            analysisStatus = await GetAnalysisStatus.ExecuteAsync(actor, cancellationToken);
            drafts = await ListDrafts.ExecuteAsync(actor, DraftsCursor, PageSize, Filter, cancellationToken);
            statementCounts = await GetStatementCounts.ExecuteAsync(actor, cancellationToken);
            briefCounts = await GetBriefCounts.ExecuteAsync(actor, cancellationToken);
            relationshipCounts = await GetRelationshipCounts.ExecuteAsync(actor, cancellationToken);
            openReports = await CountReports.ExecuteAsync(actor, cancellationToken);
            if (collection is not null)
            {
                configuration = collection.GetConfiguration(actor);
                health = await collection.GetSourceHealthAsync(actor, cancellationToken);
            }
            if (canPublish) await LoadPublicationAsync(cancellationToken);
            activity = (await GetActivity.ExecuteAsync(actor, cancellationToken: cancellationToken)).Select(Describe).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ArgumentException)
        {
            overview = null;
            error = "This page link is no longer valid. Go back to the first page of the list.";
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "The review overview could not load.");
            overview = null;
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

    private IReadOnlyList<FilterTab> DraftTabs => DraftFilterTabs.For(
        Filter, name => Href(ChangesCursor, name, null, "drafts"), overview!.DraftsNeedingAction, overview.ApprovedDrafts);

    private string? FilterName => DraftFilterNames.NameOf(Filter);

    private string? ChangesHref(string? cursor) =>
        cursor is null ? null : Href(cursor, FilterName, DraftsCursor, "new-changes");

    private string? DraftsHref(string? cursor) =>
        cursor is null ? null : Href(ChangesCursor, FilterName, cursor, "drafts");

    private static string Href(string? changesCursor, string? filter, string? draftsCursor, string fragment)
    {
        var query = new List<string>();
        if (changesCursor is not null) query.Add("changes=" + Uri.EscapeDataString(changesCursor));
        if (filter is not null) query.Add("drafts=" + filter);
        if (draftsCursor is not null) query.Add("draftsCursor=" + Uri.EscapeDataString(draftsCursor));
        return "/Review" + (query.Count == 0 ? "" : "?" + string.Join('&', query)) + "#" + fragment;
    }

    private (string Title, string Text) DraftsEmpty => Filter switch
    {
        DraftStatusFilter.NeedsAction => ("No drafts need action", "Every draft is approved, or no draft exists yet."),
        DraftStatusFilter.Approved => ("No approved drafts", "Approved drafts show here. Approval keeps them private."),
        _ => ("No drafts yet", "Start a draft from a new change.")
    };

    private static string Count(int value, string singular, string plural) =>
        $"{value} {(value == 1 ? singular : plural)}";
}
