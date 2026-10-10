using System.Collections.Immutable;
using System.Globalization;
using CivicLens.Application.Analysis;
using CivicLens.Application.Documents;
using CivicLens.Application.Review;
using CivicLens.Core.Analysis;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Host.Components.Evidence;
using CivicLens.Host.Components.Layout;
using CivicLens.Host.Components.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace CivicLens.Host.Review;

public sealed partial class DraftEditor
{
    private const string SaveForm = "save";
    private const string DecideForm = "decide";
    private const int PreselectedPassageLimit = 32;

    private readonly string saveKey = Guid.NewGuid().ToString("N");
    private readonly string decisionKey = Guid.NewGuid().ToString("N");
    private ReviewActor actor = default!;
    private DocumentChangeReview? review;
    private AnalysisRun? draftRun;
    private DocumentComparison? comparison;
    private DocumentExtraction? before;
    private DocumentExtraction? after;
    private ImmutableArray<EvidenceOption> evidenceOptions = [];
    private IReadOnlyList<SavedCitation> savedCitations = [];
    private string? error;
    private string? statusMessage;
    private bool isConflict;
    private bool isDraftConflict;
    private bool hasUnsavedInput;
    private bool notFound;

    [Inject] private ReviewActorAccessor Actors { get; set; } = default!;
    [Inject] private GetDocumentChangeReview GetReview { get; set; } = default!;
    [Inject] private GetDocumentComparison GetComparison { get; set; } = default!;
    [Inject] private GetDocumentCitation GetCitation { get; set; } = default!;
    [Inject] private SaveDocumentChangeDraft SaveDraft { get; set; } = default!;
    [Inject] private DecideDocumentChangeReview DecideReview { get; set; } = default!;
    [Inject] private ReviewCatalog Catalog { get; set; } = default!;
    [Inject] private GetDocumentChangeAnalysisRun GetRun { get; set; } = default!;
    [Inject] private IDocumentExtractionStore Extractions { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [CascadingParameter] private HttpContext HttpContext { get; set; } = default!;
    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = default!;

    [Parameter] public string DraftId { get; set; } = string.Empty;
    [SupplyParameterFromQuery(Name = "status")] private string? Status { get; set; }

    [SupplyParameterFromForm(FormName = SaveForm)] private DraftInput? Input { get; set; }
    [SupplyParameterFromForm(FormName = SaveForm, Name = "expectedRevisionNumber")] private int SaveExpectedRevision { get; set; }
    [SupplyParameterFromForm(FormName = SaveForm, Name = "idempotencyKey")] private string? SaveKey { get; set; }
    [SupplyParameterFromForm(FormName = DecideForm)] private DecisionInput? Decision { get; set; }
    [SupplyParameterFromForm(FormName = DecideForm, Name = "expectedRevisionNumber")] private int DecideExpectedRevision { get; set; }
    [SupplyParameterFromForm(FormName = DecideForm, Name = "expectedReviewStateVersion")] private int DecideExpectedStateVersion { get; set; }
    [SupplyParameterFromForm(FormName = DecideForm, Name = "idempotencyKey")] private string? DecideKey { get; set; }
    [SupplyParameterFromForm(FormName = DecideForm, Name = "kind")] private string? Kind { get; set; }

    private CancellationToken RequestAborted => HttpContext.RequestAborted;
    private DocumentChangeDraftRevision Revision => review!.CurrentRevision;
    private bool CanDecide => !hasUnsavedInput && !isConflict && error is null;
    private DocumentChangeReviewStatus CurrentStatus =>
        DocumentChangeReviewPolicy.GetCurrentStatus(Revision, review!.Decisions, review.UnresolvedConcerns);
    private ImmutableArray<string> OfficialIds => [.. Catalog.OfficialIds.Order(StringComparer.Ordinal)];
    private ImmutableArray<string> IssueIds => [.. Catalog.IssueIds.Order(StringComparer.Ordinal)];
    private int UnverifiedCitations => Revision.Citations.Length - savedCitations.Count;

    protected override async Task OnInitializedAsync()
    {
        actor = Actors.GetActor((await AuthenticationState).User);
        if (!HttpMethods.IsPost(HttpContext.Request.Method)) statusMessage = StatusText();
        await LoadAsync();
    }

    private string? StatusText() => Status switch
    {
        "saved" => "The new revision is saved. Review it before you record a decision.",
        "decided" => "Your decision on this revision is saved.",
        _ => null
    };

    private async Task LoadAsync()
    {
        try
        {
            review = await GetReview.ExecuteAsync(actor, DraftId, RequestAborted);
            if (review is null)
            {
                ShowNotFound();
                return;
            }
            if (ReviewAuthor.IsAnalysis(review.Revisions[0].AuthorSubject))
                draftRun = await GetRun.ExecuteAsync(actor, DraftId, RequestAborted);
            comparison = await GetComparison.ExecuteAsync(Revision.ComparisonId, RequestAborted);
            if (comparison is null)
            {
                error = "The saved comparison for this draft is unavailable. The wording still shows so you can recover it.";
                Input ??= DraftInput.From(Revision, []);
                return;
            }
            before = await Extractions.GetAsync(comparison.BeforeExtractionId, RequestAborted);
            after = await Extractions.GetAsync(comparison.AfterExtractionId, RequestAborted);
            evidenceOptions = EvidenceOption.Build(comparison, Revision.Citations, before, after);
            Input ??= DraftInput.From(Revision, evidenceOptions);
            savedCitations = await VerifyCitationsAsync();
        }
        catch (OperationCanceledException) when (RequestAborted.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { Navigation.NavigateTo("/access-denied"); }
        catch (ArgumentException) { ShowNotFound(); }
        catch (Exception)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            error ??= "The review workspace could not load this record. Refresh the page to try again.";
        }
    }

    private void ShowNotFound()
    {
        review = null;
        notFound = true;
        HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    private async Task<IReadOnlyList<SavedCitation>> VerifyCitationsAsync()
    {
        var verified = new List<SavedCitation>();
        foreach (var binding in Revision.Citations)
        {
            var quote = await ReadQuoteAsync(binding);
            if (quote is not null) verified.Add(new SavedCitation(binding, quote, CitationLabel(binding), PassageKey(binding)));
        }
        return verified;
    }

    private async Task<string?> ReadQuoteAsync(DocumentChangeCitation binding)
    {
        try
        {
            var retained = new[] { before, after }.FirstOrDefault(item => item?.ExtractionId == binding.ExtractionId);
            var span = retained is null
                ? await GetCitation.ExecuteAsync(binding.ExtractionId, binding.Start, binding.Length, RequestAborted)
                : new DocumentTextSpan(retained, binding.Start, binding.Length);
            return span.Quote;
        }
        catch (ArgumentException) { return null; }
    }

    private string CitationLabel(DocumentChangeCitation binding)
    {
        var version = binding.ExtractionId == comparison!.BeforeExtractionId ? "Earlier version"
            : binding.ExtractionId == comparison.AfterExtractionId ? "Later version" : "Retained source text";
        return Revision.ChangeDateEvidence == binding ? $"Change date evidence, {version.ToLowerInvariant()}" : version;
    }

    private string? PassageKey(DocumentChangeCitation binding)
    {
        for (var index = 0; index < comparison!.Hunks.Length; index++)
        {
            var hunk = comparison.Hunks[index];
            if (binding.ExtractionId == comparison.BeforeExtractionId && Covers(hunk.BeforeStart, hunk.BeforeLength, binding) ||
                binding.ExtractionId == comparison.AfterExtractionId && Covers(hunk.AfterStart, hunk.AfterLength, binding))
                return DocumentComparisonView.LinkKey(comparison.ComparisonId, index);
        }
        return null;
    }

    private static bool Covers(int start, int length, DocumentChangeCitation binding) =>
        binding.Start >= start && binding.Start + binding.Length <= start + length;

    private async Task SaveAsync()
    {
        var target = await TrySaveAsync();
        if (target is not null) Navigation.NavigateTo(target);
    }

    private async Task<string?> TrySaveAsync()
    {
        if (review is null) return null;
        try
        {
            if (comparison is null) throw new ArgumentException("The saved comparison is unavailable.");
            _ = await SaveDraft.ExecuteAsync(actor, BuildSaveRequest(Input ?? new DraftInput()), RequestAborted);
            return $"/Review/{DraftId}?status=saved";
        }
        catch (OperationCanceledException) when (RequestAborted.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { return "/access-denied"; }
        catch (DocumentChangeReviewConflictException)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
            isConflict = true;
            isDraftConflict = true;
            statusMessage = "Someone saved a newer revision. Your wording is kept below. Compare both versions, then save again.";
            await LoadAsync();
        }
        catch (ArgumentException)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            error = "Some fields or selections are not valid. Correct them and save again.";
        }
        catch (Exception)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            error = "The revision could not be saved. Your wording is kept in the form. Try again.";
        }
        hasUnsavedInput = true;
        return null;
    }

    private SaveDocumentChangeDraftRequest BuildSaveRequest(DraftInput input)
    {
        var citations = ResolveCitations(input.EvidenceSelection);
        DateOnly? changeDate = null;
        DocumentChangeCitation? changeDateEvidence = null;
        if (!string.IsNullOrWhiteSpace(input.ChangeDate))
        {
            if (!DateOnly.TryParseExact(input.ChangeDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsedDate))
                throw new ArgumentException("Enter the actual change date in the requested format.");
            changeDate = parsedDate;
            var selected = FindOption(input.DateEvidenceSelection)
                ?? throw new ArgumentException("Select the retained passage that supports the actual change date.");
            if (!citations.Contains(selected.Citation))
            {
                if (citations.Length >= DocumentChangeDraftRevision.MaximumCitations) throw new ArgumentException("Remove a citation before binding the change-date evidence.");
                citations = citations.Add(selected.Citation);
            }
            changeDateEvidence = selected.Citation;
        }
        return new SaveDocumentChangeDraftRequest(DraftId, SaveExpectedRevision, input.Headline ?? "", input.Summary ?? "",
            EmptyToNull(input.Significance), EmptyToNull(input.Limits), input.Institution ?? "", changeDate, changeDateEvidence,
            [.. input.OfficialIds ?? []], [.. input.IssueIds ?? []], citations, SaveKey ?? "", input.ChangeDateChecked == true);
    }

    private ImmutableArray<DocumentChangeCitation> ResolveCitations(string[]? keys)
    {
        var selections = keys ?? [];
        if (selections.Length > DocumentChangeDraftRevision.MaximumCitations || selections.Distinct(StringComparer.Ordinal).Count() != selections.Length)
            throw new ArgumentException($"Select no more than {DocumentChangeDraftRevision.MaximumCitations} distinct evidence passages.");
        return [.. selections.Select(key => (FindOption(key)
            ?? throw new ArgumentException("An evidence selection is no longer available. Refresh and choose it again.")).Citation)];
    }

    private EvidenceOption? FindOption(string? key) =>
        evidenceOptions.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.Ordinal));

    private async Task DecideAsync()
    {
        var target = await TryDecideAsync();
        if (target is not null) Navigation.NavigateTo(target);
    }

    private async Task<string?> TryDecideAsync()
    {
        if (review is null) return null;
        try
        {
            if (!Enum.TryParse<ReviewDecisionKind>(Kind, ignoreCase: false, out var kind) || !Enum.IsDefined(kind))
                throw new ArgumentException("Choose a valid review action.");
            var request = new DecideDocumentChangeReviewRequest(DraftId, DecideExpectedRevision, DecideExpectedStateVersion,
                kind, EmptyToNull(Decision?.Note), [.. Decision?.ResolvedDecisionIds ?? []], DecideKey ?? "");
            _ = await DecideReview.ExecuteAsync(actor, request, RequestAborted);
            return $"/Review/{DraftId}?status=decided";
        }
        catch (OperationCanceledException) when (RequestAborted.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { return "/access-denied"; }
        catch (DocumentChangeReviewConflictException)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
            isConflict = true;
            statusMessage = "The revision or review state changed after you opened this page. Check the latest state before you record a decision.";
            Input = null;
            await LoadAsync();
        }
        catch (ArgumentException)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            error = "The decision needs valid details. Requesting changes and withdrawing approval need a reason. Approval must resolve every open concern.";
        }
        catch (Exception)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            error = "The decision could not be saved. The current review state shows below. Try again.";
        }
        return null;
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private string SelectedTab => hasUnsavedInput || isConflict ? "edit" : "saved";

    private IReadOnlyList<Crumb> Crumbs =>
        [new("Review", "/Review"), new(review is not null ? Headline : notFound ? "Draft not found" : "Draft")];

    private IReadOnlyList<TabDefinition> AccountTabs =>
        [new("saved", $"Saved revision {Revision.RevisionNumber}"), new("edit", "Edit wording")];

    private string Headline => DraftStatusDisplay.DraftName(Revision.Headline);

    private string ObservedText(DocumentExtraction? extraction) =>
        extraction is null ? "Unknown" : Moment.Full(extraction.SourceAttempt.ObservedAt);

    private IReadOnlyList<Fact> RecordFacts(string? institution, string changeDate, IEnumerable<string>? officials,
        IEnumerable<string>? issues) =>
    [
        new("Institution", string.IsNullOrWhiteSpace(institution) ? "Not set" : institution),
        new("Change date", changeDate),
        new("Earlier version observed", ObservedText(before)),
        new("Later version observed", ObservedText(after)),
        new("Officials", JoinOrNone(officials)),
        new("Issues", JoinOrNone(issues))
    ];

    private IReadOnlyList<Fact> SavedFacts => RecordFacts(Revision.Institution,
        Revision.ChangeDate is { } date ? Moment.Iso(date) : "Not established", Revision.OfficialIds, Revision.IssueIds);

    private IReadOnlyList<Fact> UnsavedFacts => RecordFacts(Input!.Institution,
        string.IsNullOrWhiteSpace(Input.ChangeDate) ? "Not established" : Input.ChangeDate, Input.OfficialIds, Input.IssueIds);

    private IEnumerable<EvidenceOption> UnsavedEvidence => evidenceOptions.Where(option =>
        (Input!.EvidenceSelection ?? []).Contains(option.Key) ||
        !string.IsNullOrWhiteSpace(Input.ChangeDate) && Input.DateEvidenceSelection == option.Key);

    private static string JoinOrNone(IEnumerable<string>? values)
    {
        var text = string.Join(", ", values ?? []);
        return text.Length == 0 ? "None selected" : text;
    }

    private IReadOnlyList<(string Label, string Value)> TechnicalItems =>
    [
        ("Source", before?.SourceAttempt.SourceId ?? after?.SourceAttempt.SourceId ?? "Unknown"),
        ("Requested URL", before?.SourceAttempt.RequestedUrl ?? after?.SourceAttempt.RequestedUrl ?? "Unknown"),
        ("Earlier final URL", before?.SourceAttempt.FinalUrl ?? "Unknown"),
        ("Later final URL", after?.SourceAttempt.FinalUrl ?? "Unknown"),
        ("Comparison ID", Revision.ComparisonId),
        ("Earlier extraction", comparison!.BeforeExtractionId),
        ("Later extraction", comparison.AfterExtractionId),
        .. DraftRunItems
    ];

    private bool IsAiRevision => ReviewAuthor.IsAnalysis(Revision.AuthorSubject);

    private bool HasUnconfirmedAiChangeDate => IsAiRevision && Revision.ChangeDate is not null;

    private IEnumerable<(string Label, string Value)> DraftRunItems => draftRun is null ? [] :
    [
        ("AI draft model", draftRun.Model),
        ("AI prompt version", draftRun.PromptVersion),
        ("AI analysis run", draftRun.RunId),
        ("AI tokens", $"{draftRun.Usage.InputTokens:N0} input, {draftRun.Usage.OutputTokens:N0} output")
    ];

    private IReadOnlyList<HistoryEntry> History =>
    [
        .. review!.Revisions.Select(item => new HistoryEntry(item.CreatedAtUtc,
            item.RevisionNumber > 1 ? $"Revision {item.RevisionNumber} saved"
                : ReviewAuthor.IsAnalysis(item.AuthorSubject) ? "AI draft written" : "Draft started", null,
            item.AuthorSubject, DraftStatusDisplay.DraftName(item.Headline)))
            .Concat(review.Decisions.Select(item => new HistoryEntry(item.CreatedAtUtc, $"on revision {item.RevisionNumber}",
                item.Kind, item.ActorSubject, DecisionNote(item))))
            .OrderByDescending(item => item.When)
    ];

    private static string? DecisionNote(ReviewDecision decision) => decision.ResolvedDecisionIds.Length switch
    {
        0 => decision.Note,
        1 => $"{decision.Note} Resolved 1 concern.",
        var count => $"{decision.Note} Resolved {count} concerns."
    };

    private string Who(string subject) => ReviewAuthor.IsAnalysis(subject) ? "AI drafter"
        : subject == actor.Subject ? "You" : "Another reviewer";

    public sealed class DraftInput
    {
        public string? Headline { get; set; } = "";
        public string? Summary { get; set; } = "";
        public string? Significance { get; set; }
        public string? Limits { get; set; }
        public string? Institution { get; set; } = "";
        public string? ChangeDate { get; set; } = "";
        public string? DateEvidenceSelection { get; set; } = "";
        public string[]? EvidenceSelection { get; set; } = [];
        public string[]? OfficialIds { get; set; } = [];
        public string[]? IssueIds { get; set; } = [];
        public bool? ChangeDateChecked { get; set; }

        internal static DraftInput From(DocumentChangeDraftRevision revision, ImmutableArray<EvidenceOption> options) => new()
        {
            Headline = revision.Headline,
            Summary = revision.Summary,
            Significance = revision.Significance ?? "",
            Limits = revision.Limits ?? "",
            Institution = revision.Institution,
            ChangeDate = revision.ChangeDate is { } date ? Moment.Iso(date) : "",
            DateEvidenceSelection = KeyOf(revision.ChangeDateEvidence, options),
            EvidenceSelection = [.. revision.Citations.Select(citation => KeyOf(citation, options)).Where(key => key.Length > 0)],
            OfficialIds = [.. revision.OfficialIds],
            IssueIds = [.. revision.IssueIds]
        };

        private static string KeyOf(DocumentChangeCitation? citation, ImmutableArray<EvidenceOption> options) =>
            options.FirstOrDefault(option => option.Citation == citation)?.Key ?? "";
    }

    public sealed class DecisionInput
    {
        public string? Note { get; set; }
        public string[]? ResolvedDecisionIds { get; set; } = [];
    }

    internal sealed record EvidenceOption(string Key, string Label, string Quote, DocumentChangeCitation Citation, string Link)
    {
        public static ImmutableArray<EvidenceOption> Build(DocumentComparison comparison)
        {
            var options = ImmutableArray.CreateBuilder<EvidenceOption>();
            for (var index = 0; index < comparison.Hunks.Length; index++)
            {
                var hunk = comparison.Hunks[index];
                var link = DocumentComparisonView.LinkKey(comparison.ComparisonId, index);
                if (hunk.BeforeLength > 0)
                    options.Add(new($"b:{index}", $"Earlier text, passage {index + 1}", hunk.BeforeText,
                        new DocumentChangeCitation(comparison.BeforeExtractionId, hunk.BeforeStart, hunk.BeforeLength), link));
                if (hunk.AfterLength > 0)
                    options.Add(new($"a:{index}", $"Later text, passage {index + 1}", hunk.AfterText,
                        new DocumentChangeCitation(comparison.AfterExtractionId, hunk.AfterStart, hunk.AfterLength), link));
            }
            return options.ToImmutable();
        }

        public static ImmutableArray<EvidenceOption> Build(DocumentComparison comparison,
            ImmutableArray<DocumentChangeCitation> saved, DocumentExtraction? before, DocumentExtraction? after)
        {
            var options = Build(comparison).ToBuilder();
            foreach (var citation in saved.Where(citation => options.All(option => option.Citation != citation)))
            {
                var earlier = citation.ExtractionId == comparison.BeforeExtractionId;
                var extraction = earlier ? before : after;
                if (extraction is null || citation.Start < 0 || citation.Start > extraction.Text.Length - citation.Length) continue;
                var index = ContainingHunk(comparison, citation, earlier);
                options.Add(new($"{(earlier ? "xb" : "xa")}:{citation.Start}:{citation.Length}",
                    $"{(earlier ? "Earlier" : "Later")} text, cited passage{(index is { } number ? $" in passage {number + 1}" : "")}",
                    extraction.Text.Substring(citation.Start, citation.Length), citation,
                    DocumentComparisonView.LinkKey(comparison.ComparisonId, index ?? 0)));
            }
            return options.ToImmutable();
        }

        private static int? ContainingHunk(DocumentComparison comparison, DocumentChangeCitation citation, bool earlier)
        {
            for (var index = 0; index < comparison.Hunks.Length; index++)
            {
                var hunk = comparison.Hunks[index];
                var (start, text) = earlier ? (hunk.BeforeContextStart, hunk.BeforeContext) : (hunk.AfterContextStart, hunk.AfterContext);
                if (citation.Start >= start && citation.Start + citation.Length <= start + text.Length) return index;
            }
            return null;
        }
    }

    private sealed record SavedCitation(DocumentChangeCitation Binding, string Quote, string Label, string? Link);

    private sealed record HistoryEntry(DateTimeOffset When, string Event, ReviewDecisionKind? Decision, string ActorSubject,
        string? Note);
}
