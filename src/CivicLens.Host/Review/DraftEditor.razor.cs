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

namespace CivicLens.Host.Review;

public sealed partial class DraftEditor
{
    private const int PreselectedPassageLimit = 32;

    private DocumentChangeReview? review;
    private AnalysisRun? draftRun;
    private DocumentComparison? comparison;
    private DocumentExtraction? before;
    private DocumentExtraction? after;
    private ImmutableArray<EvidenceOption> evidenceOptions = [];
    private IReadOnlyList<SavedCitation> savedCitations = [];
    private bool isDraftConflict;

    [Inject] private GetDocumentChangeReview GetReview { get; set; } = default!;
    [Inject] private GetDocumentComparison GetComparison { get; set; } = default!;
    [Inject] private GetDocumentCitation GetCitation { get; set; } = default!;
    [Inject] private SaveDocumentChangeDraft SaveDraft { get; set; } = default!;
    [Inject] private DecideDocumentChangeReview DecideReview { get; set; } = default!;
    [Inject] private ReviewCatalog Catalog { get; set; } = default!;
    [Inject] private GetDocumentChangeAnalysisRun GetRun { get; set; } = default!;
    [Inject] private IDocumentExtractionStore Extractions { get; set; } = default!;

    [Parameter] public string DraftId { get; set; } = string.Empty;

    [SupplyParameterFromForm(FormName = SaveForm)] private DraftInput? Input { get; set; }

    private DocumentChangeDraftRevision Revision => review!.CurrentRevision;
    private ReviewStatus CurrentStatus =>
        ReviewPolicy.GetCurrentStatus(Revision.RevisionNumber, review!.Decisions, review.UnresolvedConcerns);
    private ImmutableArray<string> OfficialIds => [.. Catalog.OfficialIds.Order(StringComparer.Ordinal)];
    private ImmutableArray<string> IssueIds => [.. Catalog.IssueIds.Order(StringComparer.Ordinal)];
    private int UnverifiedCitations => Revision.Citations.Length - savedCitations.Count;

    protected override Task LoadAsync() => LoadGuardedAsync(ReadRecordAsync);

    private async Task ReadRecordAsync()
    {
        review = await GetReview.ExecuteAsync(actor, DraftId, RequestAborted);
        if (review is null)
        {
            ShowNotFound();
            return;
        }
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

    protected override void ShowNotFound()
    {
        review = null;
        base.ShowNotFound();
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

    private async Task<string?> ReadQuoteAsync(SourceSpan binding)
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

    private string CitationLabel(SourceSpan binding)
    {
        var version = binding.ExtractionId == comparison!.BeforeExtractionId ? "Earlier version"
            : binding.ExtractionId == comparison.AfterExtractionId ? "Later version" : "Retained source text";
        return Revision.ChangeDateEvidence == binding ? $"Change date evidence, {version.ToLowerInvariant()}" : version;
    }

    private string? PassageKey(SourceSpan binding)
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

    private static bool Covers(int start, int length, SourceSpan binding) =>
        binding.Start >= start && binding.Start + binding.Length <= start + length;

    private async Task SaveAsync() => NavigateWhenSet(await TrySaveAsync());

    private async Task<string?> TrySaveAsync()
    {
        if (review is null) return null;
        var target = await RunCommandAsync(PersistDraftAsync, SaveFailures, ReloadAfterSaveConflictAsync);
        hasUnsavedInput = target is null;
        return target;
    }

    private async Task<string> PersistDraftAsync()
    {
        if (comparison is null) throw new ArgumentException("The saved comparison is unavailable.");
        _ = await SaveDraft.ExecuteAsync(actor, BuildSaveRequest(Input ?? new DraftInput()), RequestAborted);
        return $"/Review/{DraftId}?status=saved";
    }

    private Task ReloadAfterSaveConflictAsync()
    {
        isDraftConflict = true;
        return LoadAsync();
    }

    private SaveDocumentChangeDraftRequest BuildSaveRequest(DraftInput input)
    {
        var citations = ResolveCitations(input.EvidenceSelection);
        DateOnly? changeDate = null;
        SourceSpan? changeDateEvidence = null;
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

    private ImmutableArray<SourceSpan> ResolveCitations(string[]? keys)
    {
        var selections = keys ?? [];
        if (selections.Length > DocumentChangeDraftRevision.MaximumCitations || selections.Distinct(StringComparer.Ordinal).Count() != selections.Length)
            throw new ArgumentException($"Select no more than {DocumentChangeDraftRevision.MaximumCitations} distinct evidence passages.");
        return [.. selections.Select(key => (FindOption(key)
            ?? throw new ArgumentException("An evidence selection is no longer available. Refresh and choose it again.")).Citation)];
    }

    private EvidenceOption? FindOption(string? key) =>
        evidenceOptions.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.Ordinal));

    private async Task DecideAsync() => NavigateWhenSet(await TryDecideAsync());

    private async Task<string?> TryDecideAsync()
    {
        if (review is null) return null;
        return await RunCommandAsync(RecordDecisionAsync, DecideFailures, ReloadAfterDecideConflictAsync);
    }

    private async Task<string> RecordDecisionAsync()
    {
        var request = new DecideDocumentChangeReviewRequest(DraftId, DecideExpectedRevision, DecideExpectedStateVersion,
            ParseDecisionKind(Kind), EmptyToNull(Decision?.Note), [.. Decision?.ResolvedDecisionIds ?? []], DecideKey ?? "");
        _ = await DecideReview.ExecuteAsync(actor, request, RequestAborted);
        return $"/Review/{DraftId}?status=decided";
    }

    private Task ReloadAfterDecideConflictAsync()
    {
        Input = null;
        return LoadAsync();
    }

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

    private IReadOnlyList<ReviewHistoryEntry> History => ReviewHistoryEntry.Newest(
        review!.Revisions.Select(item => ReviewHistoryEntry.ForRevision(item.CreatedAtUtc, item.RevisionNumber, item.AuthorSubject, item.Headline)),
        review.Decisions);

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

        private static string KeyOf(SourceSpan? citation, ImmutableArray<EvidenceOption> options) =>
            options.FirstOrDefault(option => option.Citation == citation)?.Key ?? "";
    }

    internal sealed record EvidenceOption(string Key, string Label, string Quote, SourceSpan Citation, string Link)
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
                        new SourceSpan(comparison.BeforeExtractionId, hunk.BeforeStart, hunk.BeforeLength), link));
                if (hunk.AfterLength > 0)
                    options.Add(new($"a:{index}", $"Later text, passage {index + 1}", hunk.AfterText,
                        new SourceSpan(comparison.AfterExtractionId, hunk.AfterStart, hunk.AfterLength), link));
            }
            return options.ToImmutable();
        }

        public static ImmutableArray<EvidenceOption> Build(DocumentComparison comparison,
            ImmutableArray<SourceSpan> saved, DocumentExtraction? before, DocumentExtraction? after)
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

        private static int? ContainingHunk(DocumentComparison comparison, SourceSpan citation, bool earlier)
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

    private sealed record SavedCitation(SourceSpan Binding, string Quote, string Label, string? Link);
}
