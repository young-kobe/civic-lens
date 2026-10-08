using System.Collections.Immutable;
using System.Globalization;
using CivicLens.Application.Documents;
using CivicLens.Application.Review;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Host.Review;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CivicLens.Host.Pages.Review;

[Authorize]
public sealed class EditorModel(GetDocumentChangeReview getReview, GetDocumentComparison getComparison,
    GetDocumentCitation getCitation, SaveDocumentChangeDraft saveDraft,
    DecideDocumentChangeReview decideReview, ReviewCatalog catalog,
    IDocumentExtractionStore extractions, ReviewActorAccessor actorAccessor) : PageModel
{
    [BindProperty]
    public DraftInput Input { get; set; } = new();

    [BindProperty]
    public DecisionInput Decision { get; set; } = new();

    public DocumentChangeReview? Review { get; private set; }
    public DocumentComparison? Comparison { get; private set; }
    public ImmutableArray<DocumentTextSpan> Citations { get; private set; } = [];
    public ImmutableArray<EvidenceOption> EvidenceOptions { get; private set; } = [];
    public ImmutableArray<string> AvailableOfficialIds { get; } = catalog.OfficialIds.Order(StringComparer.Ordinal).ToImmutableArray();
    public ImmutableArray<string> AvailableIssueIds { get; } = catalog.IssueIds.Order(StringComparer.Ordinal).ToImmutableArray();
    public string? SourceId { get; private set; }
    public string? RequestedUrl { get; private set; }
    public string? BeforeFinalUrl { get; private set; }
    public string? AfterFinalUrl { get; private set; }
    public DateTimeOffset? BeforeObservedAtUtc { get; private set; }
    public DateTimeOffset? AfterObservedAtUtc { get; private set; }
    public string? Error { get; private set; }
    public string? Status { get; private set; }
    public bool IsConflict { get; private set; }
    public bool HasUnsavedInput { get; private set; }
    public bool CanDecide => !HasUnsavedInput && !IsConflict && Error is null;
    public bool IsDraftConflict { get; private set; }
    public string SaveIdempotencyKey { get; private set; } = Guid.NewGuid().ToString("N");
    public string DecisionIdempotencyKey { get; private set; } = Guid.NewGuid().ToString("N");
    public string ActorSubject { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync(string draftId, string? status, CancellationToken cancellationToken)
    {
        if (status == "saved") Status = "saved";
        if (status == "decided") Status = "Review decision saved for this revision.";
        return await LoadPageAsync(draftId, preserveInput: false, cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveAsync(string draftId, int expectedRevisionNumber,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        try
        {
            var actor = actorAccessor.GetActor(User);
            var current = await getReview.ExecuteAsync(actor, draftId, cancellationToken);
            if (current is null) return NotFound();
            var comparison = await getComparison.ExecuteAsync(current.CurrentRevision.ComparisonId, cancellationToken);
            if (comparison is null) throw new ArgumentException("The saved comparison is unavailable.");
            var evidenceOptions = BuildEvidenceOptions(comparison);
            var citations = ResolveCitations(Input.EvidenceSelection, evidenceOptions);
            DateOnly? changeDate = null;
            DocumentChangeCitation? changeDateEvidence = null;
            if (!string.IsNullOrWhiteSpace(Input.ChangeDate))
            {
                if (!DateOnly.TryParseExact(Input.ChangeDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsedDate))
                    throw new ArgumentException("Enter the actual change date in the requested format.");
                changeDate = parsedDate;
                var selected = evidenceOptions.FirstOrDefault(option =>
                    string.Equals(option.Key, Input.DateEvidenceSelection, StringComparison.Ordinal));
                if (selected is null)
                    throw new ArgumentException("Select the retained passage that supports the actual change date.");
                if (!citations.Contains(selected.Citation))
                {
                    if (citations.Length >= 64) throw new ArgumentException("Remove a citation before binding the change-date evidence.");
                    citations = citations.Add(selected.Citation);
                }
                changeDateEvidence = selected.Citation;
            }
            var request = new SaveDocumentChangeDraftRequest(draftId, expectedRevisionNumber,
                Input.Headline ?? "", Input.Summary ?? "", EmptyToNull(Input.Significance),
                EmptyToNull(Input.Limits), Input.Institution ?? "", changeDate, changeDateEvidence,
                (Input.OfficialIds ?? []).ToImmutableArray(), (Input.IssueIds ?? []).ToImmutableArray(),
                citations, idempotencyKey);
            _ = await saveDraft.ExecuteAsync(actor, request, cancellationToken);
            return RedirectToPage("/Review/Editor", new { draftId, status = "saved" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (DocumentChangeReviewConflictException)
        {
            Response.StatusCode = StatusCodes.Status409Conflict;
            IsConflict = true;
            IsDraftConflict = true;
            Status = "A newer revision was saved by another reviewer. The latest comparison is shown below; your unsaved wording remains in the fields. Review both versions, then save again.";
            return await LoadPageAsync(draftId, preserveInput: true, cancellationToken);
        }
        catch (ArgumentException)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            Error = "Some draft fields or selections are invalid. Correct them and try saving again.";
            return await LoadPageAsync(draftId, preserveInput: true, cancellationToken);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (Exception)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            Error = "The revision could not be saved. Your wording remains in the form; try again.";
            return await LoadPageAsync(draftId, preserveInput: true, cancellationToken);
        }
    }

    public async Task<IActionResult> OnPostDecideAsync(string draftId, int expectedRevisionNumber,
        int expectedReviewStateVersion, string idempotencyKey, string kind,
        CancellationToken cancellationToken)
    {
        try
        {
            var actor = actorAccessor.GetActor(User);
            if (!Enum.TryParse<ReviewDecisionKind>(kind, ignoreCase: false, out var decisionKind) ||
                !Enum.IsDefined(decisionKind))
                throw new ArgumentException("Choose a valid review action.");
            var request = new DecideDocumentChangeReviewRequest(draftId, expectedRevisionNumber,
                expectedReviewStateVersion, decisionKind, EmptyToNull(Decision.Note),
                (Decision.ResolvedDecisionIds ?? []).ToImmutableArray(), idempotencyKey);
            _ = await decideReview.ExecuteAsync(actor, request, cancellationToken);
            return RedirectToPage("/Review/Editor", new { draftId, status = "decided" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (DocumentChangeReviewConflictException)
        {
            Response.StatusCode = StatusCodes.Status409Conflict;
            IsConflict = true;
            Status = "The revision or review state changed after you opened this page. Refresh the latest state before recording a decision.";
            return await LoadPageAsync(draftId, preserveInput: false, cancellationToken);
        }
        catch (ArgumentException)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            Error = "The decision needs valid details. Request changes and withdrawal require a reason; approval must resolve every outstanding concern.";
            return await LoadPageAsync(draftId, preserveInput: false, cancellationToken);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (Exception)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            Error = "The decision could not be saved. The current review state is shown below; try again.";
            return await LoadPageAsync(draftId, preserveInput: false, cancellationToken);
        }
    }

    private async Task<IActionResult> LoadPageAsync(string draftId, bool preserveInput,
        CancellationToken cancellationToken)
    {
        HasUnsavedInput = preserveInput;
        try
        {
            var actor = actorAccessor.GetActor(User);
            ActorSubject = actor.Subject;
            Review = await getReview.ExecuteAsync(actor, draftId, cancellationToken);
            if (Review is null) return NotFound();

            if (!preserveInput)
            {
                Input = new DraftInput
                {
                    Headline = Review.CurrentRevision.Headline,
                    Summary = Review.CurrentRevision.Summary,
                    Significance = Review.CurrentRevision.Significance ?? "",
                    Limits = Review.CurrentRevision.Limits ?? "",
                    Institution = Review.CurrentRevision.Institution,
                    ChangeDate = Review.CurrentRevision.ChangeDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                    OfficialIds = Review.CurrentRevision.OfficialIds.ToArray(),
                    IssueIds = Review.CurrentRevision.IssueIds.ToArray()
                };
            }

            Comparison = await getComparison.ExecuteAsync(Review.CurrentRevision.ComparisonId, cancellationToken);
            if (Comparison is null)
            {
                Error = "The saved comparison for this draft is unavailable. The wording is still shown so you can recover it.";
                return Page();
            }
            EvidenceOptions = BuildEvidenceOptions(Comparison);
            if (!preserveInput)
            {
                Input.EvidenceSelection = Review.CurrentRevision.Citations
                    .Select(citation => GetOptionKey(citation, Comparison))
                    .Where(key => key.Length > 0)
                    .ToArray();
                Input.DateEvidenceSelection = GetOptionKey(Review.CurrentRevision.ChangeDateEvidence, Comparison);
            }

            var spans = ImmutableArray.CreateBuilder<DocumentTextSpan>();
            foreach (var citation in Review.CurrentRevision.Citations)
            {
                try
                {
                    spans.Add(await getCitation.ExecuteAsync(citation.ExtractionId,
                        citation.Start, citation.Length, cancellationToken));
                }
                catch (ArgumentException)
                {
                    // A bad retained citation must stay visible as a missing-evidence state, not be replaced by another passage.
                }
            }
            Citations = spans.ToImmutable();
            var before = await extractions.GetAsync(Comparison.BeforeExtractionId, cancellationToken);
            var after = await extractions.GetAsync(Comparison.AfterExtractionId, cancellationToken);
            if (before is not null)
            {
                SourceId = before.SourceAttempt.SourceId;
                RequestedUrl = before.SourceAttempt.RequestedUrl;
                BeforeFinalUrl = before.SourceAttempt.FinalUrl;
                BeforeObservedAtUtc = before.SourceAttempt.ObservedAt;
            }
            if (after is not null)
            {
                SourceId ??= after.SourceAttempt.SourceId;
                RequestedUrl ??= after.SourceAttempt.RequestedUrl;
                AfterFinalUrl = after.SourceAttempt.FinalUrl;
                AfterObservedAtUtc = after.SourceAttempt.ObservedAt;
            }
            return Page();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException) { return NotFound(); }
        catch (Exception)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            Error ??= "The review workspace could not load this record. Try refreshing the page.";
            return Page();
        }
    }

    private static ImmutableArray<EvidenceOption> BuildEvidenceOptions(DocumentComparison comparison)
    {
        var options = ImmutableArray.CreateBuilder<EvidenceOption>();
        for (var index = 0; index < comparison.Hunks.Length; index++)
        {
            var hunk = comparison.Hunks[index];
            if (hunk.BeforeLength > 0)
                options.Add(new EvidenceOption($"b:{index}", $"Before · passage {index + 1}", hunk.BeforeText,
                    new DocumentChangeCitation(comparison.BeforeExtractionId, hunk.BeforeStart, hunk.BeforeLength)));
            if (hunk.AfterLength > 0)
                options.Add(new EvidenceOption($"a:{index}", $"After · passage {index + 1}", hunk.AfterText,
                    new DocumentChangeCitation(comparison.AfterExtractionId, hunk.AfterStart, hunk.AfterLength)));
        }
        return options.ToImmutable();
    }

    private static string GetOptionKey(DocumentChangeCitation? citation, DocumentComparison? comparison)
    {
        if (citation is null || comparison is null) return "";
        var option = BuildEvidenceOptions(comparison).FirstOrDefault(candidate => candidate.Citation == citation);
        return option?.Key ?? "";
    }

    private static ImmutableArray<DocumentChangeCitation> ResolveCitations(string[]? keys,
        ImmutableArray<EvidenceOption> options)
    {
        var selections = keys ?? [];
        if (selections.Length > 64 || selections.Distinct(StringComparer.Ordinal).Count() != selections.Length)
            throw new ArgumentException("Select no more than 64 distinct evidence passages.");
        var output = ImmutableArray.CreateBuilder<DocumentChangeCitation>(selections.Length);
        foreach (var key in selections)
        {
            var option = options.FirstOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.Ordinal));
            if (option is null) throw new ArgumentException("An evidence selection is no longer available. Refresh and choose it again.");
            output.Add(option.Citation);
        }
        return output.ToImmutable();
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    public sealed class DraftInput
    {
        public string Headline { get; set; } = "";
        public string Summary { get; set; } = "";
        public string? Significance { get; set; }
        public string? Limits { get; set; }
        public string Institution { get; set; } = "";
        public string ChangeDate { get; set; } = "";
        public string DateEvidenceSelection { get; set; } = "";
        public string[] EvidenceSelection { get; set; } = [];
        public string[]? OfficialIds { get; set; } = [];
        public string[]? IssueIds { get; set; } = [];
    }

    public sealed class DecisionInput
    {
        public string? Note { get; set; }
        public string[]? ResolvedDecisionIds { get; set; } = [];
    }

    public sealed record EvidenceOption(string Key, string Label, string Quote, DocumentChangeCitation Citation);
}
