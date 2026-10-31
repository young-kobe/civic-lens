using System.Collections.Immutable;
using CivicLens.Application.Briefs;
using CivicLens.Application.Review;
using CivicLens.Core.Briefs;
using CivicLens.Host.Components.Evidence;
using CivicLens.Host.Components.Layout;
using CivicLens.Host.Components.Ui;
using CivicLens.Host.Review;
using Microsoft.AspNetCore.Components;

namespace CivicLens.Host.Briefs;

public sealed partial class BriefEditor
{
    private BriefReviewRead? detail;
    private bool isDraftConflict;

    [Inject] private GetBriefReview GetReview { get; set; } = default!;
    [Inject] private SaveBriefDraft SaveDraft { get; set; } = default!;
    [Inject] private DecideBriefReview DecideReview { get; set; } = default!;

    [Parameter] public string RecordId { get; set; } = string.Empty;

    [SupplyParameterFromForm(FormName = SaveForm)] private BriefInput? Input { get; set; }

    private BriefRevision Revision => detail!.Review.CurrentRevision;

    private IReadOnlyList<Crumb> Crumbs =>
        [new("Review", "/Review"), new(detail is not null ? Revision.Headline : notFound ? "Brief not found" : "Brief")];

    private IReadOnlyList<TabDefinition> RecordTabs =>
        [new("saved", $"Saved revision {Revision.RevisionNumber}"), new("edit", "Edit wording")];

    protected override Task LoadAsync() => LoadGuardedAsync(ReadRecordAsync);

    private async Task ReadRecordAsync()
    {
        detail = await GetReview.ExecuteAsync(actor, RecordId, RequestAborted);
        if (detail is null)
        {
            ShowNotFound();
            return;
        }
        Input ??= BriefInput.From(Revision);
    }

    protected override void ShowNotFound()
    {
        detail = null;
        base.ShowNotFound();
    }

    private async Task SaveAsync() => NavigateWhenSet(await TrySaveAsync());

    private async Task<string?> TrySaveAsync()
    {
        if (detail is null) return null;
        var target = await RunCommandAsync(PersistDraftAsync, SaveFailures, ReloadAfterSaveConflictAsync);
        hasUnsavedInput = target is null;
        return target;
    }

    private async Task<string> PersistDraftAsync()
    {
        if (SaveExpectedRevision != Revision.RevisionNumber)
            throw new ReviewConflictException("The brief has a newer revision. Refresh before saving.");
        _ = await SaveDraft.ExecuteAsync(actor, BuildSaveRequest(Input ?? new BriefInput()), RequestAborted);
        return BriefRoutes.Editor(RecordId, "saved");
    }

    private SaveBriefDraftRequest BuildSaveRequest(BriefInput input) =>
        new(RecordId, SaveExpectedRevision, input.Headline ?? "", WrittenParagraphs(input),
            SuggestionActions.ChoiceOf(input.SuggestionAction), SaveKey ?? "");

    private static ImmutableArray<BriefParagraph> WrittenParagraphs(BriefInput input) =>
        [.. input.Paragraphs.Where(paragraph => !string.IsNullOrWhiteSpace(paragraph.Text))
            .Select(paragraph => new BriefParagraph(paragraph.Text!, [.. paragraph.CitationIds ?? []]))];

    private async Task ReloadAfterSaveConflictAsync()
    {
        isDraftConflict = true;
        await LoadAsync();
        if (detail is not null) Input?.KeepCitationsUnchangedSinceLoad(Revision.Citations);
    }

    private string CitedHeadlines(IEnumerable<string>? ids) => CitedHeadlines(ids, detail!.Cited);

    private static string CitedHeadlines(IEnumerable<string>? ids, ImmutableDictionary<string, BriefCitedRecord> cited)
    {
        var headlines = (ids ?? []).Select(id => cited.TryGetValue(id, out var record) ? record.Headline : id).ToArray();
        return headlines.Length == 0 ? "no citation chosen" : string.Join("; ", headlines);
    }

    private async Task DecideAsync() => NavigateWhenSet(await TryDecideAsync());

    private async Task<string?> TryDecideAsync()
    {
        if (detail is null) return null;
        return await RunCommandAsync(RecordDecisionAsync, DecideFailures, ReloadAfterDecideConflictAsync);
    }

    private async Task<string> RecordDecisionAsync()
    {
        var request = new DecideBriefReviewRequest(RecordId, DecideExpectedRevision, DecideExpectedStateVersion,
            ParseDecisionKind(Kind), EmptyToNull(Decision?.Note), [.. Decision?.ResolvedDecisionIds ?? []], DecideKey ?? "",
            CorrectionNoteOfApproval());
        _ = await DecideReview.ExecuteAsync(actor, request, RequestAborted);
        return BriefRoutes.Editor(RecordId, "decided");
    }

    private Task ReloadAfterDecideConflictAsync()
    {
        Input = null;
        return LoadAsync();
    }

    private static string CoverageText(BriefCoverage coverage) =>
        $"Coverage: {coverage.Considered} records considered, {coverage.SentToModel} sent to the AI drafter, {coverage.Omitted} left out, {coverage.Cited} cited.";

    private IReadOnlyList<Fact> FactsOf(BriefRevision revision) =>
    [
        new("Week of", Moment.Iso(revision.Window.WeekStart)),
        new("Cutoff release", revision.Window.CutoffReleaseNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new("Coverage", CoverageText(revision.Coverage))
    ];

    private IReadOnlyList<(string Label, string Value)> TechnicalItems =>
    [
        ("Record ID", RecordId),
        ("Fingerprint", Revision.Fingerprint)
    ];

    private IReadOnlyList<ReviewHistoryEntry> History => ReviewHistoryEntry.Newest(
        detail!.Review.Revisions.Select(item => ReviewHistoryEntry.ForRevision(item.CreatedAtUtc, item.RevisionNumber, item.AuthorSubject, item.Headline)),
        detail.Review.Decisions);

    public sealed class BriefInput
    {
        public string? Headline { get; set; } = "";
        public List<ParagraphInput> Paragraphs { get; set; } = [];
        public string? SuggestionAction { get; set; }
        public string[]? LoadedCitations { get; set; }

        internal static string LoadedKey(BriefCitation citation) => $"{citation.Id}|{citation.Kind}|{citation.RecordId}|{citation.RevisionNumber}";

        internal void KeepCitationsUnchangedSinceLoad(ImmutableArray<BriefCitation> current)
        {
            var unchanged = current.Where(citation => (LoadedCitations ?? []).Contains(LoadedKey(citation))).Select(citation => citation.Id).ToHashSet();
            foreach (var paragraph in Paragraphs) paragraph.CitationIds = [.. (paragraph.CitationIds ?? []).Where(unchanged.Contains)];
        }

        internal static BriefInput From(BriefRevision revision) => new()
        {
            Headline = revision.Headline,
            Paragraphs = [.. revision.Paragraphs.Select(paragraph => new ParagraphInput { Text = paragraph.Text, CitationIds = [.. paragraph.CitationIds] }),
                .. revision.Paragraphs.Length < BriefRevision.MaximumParagraphs ? new[] { new ParagraphInput() } : []]
        };
    }

    public sealed class ParagraphInput
    {
        public string? Text { get; set; }
        public string[]? CitationIds { get; set; } = [];
    }
}
