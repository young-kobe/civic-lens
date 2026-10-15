using System.Collections.Immutable;
using System.Globalization;
using CivicLens.Application.Analysis;
using CivicLens.Application.Issues;
using CivicLens.Application.Review;
using CivicLens.Application.Statements;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Host.Components.Evidence;
using CivicLens.Host.Components.Layout;
using CivicLens.Host.Components.Ui;
using Microsoft.AspNetCore.Components;

namespace CivicLens.Host.Review;

public sealed partial class StatementEditor
{
    private const string StartForm = "start";

    private static readonly ReviewFailureMessages StartFailures = new(
        "The statement changed after you opened this page. Reload it.",
        "Choose a statement that has no draft to start one.",
        "The draft could not be started. Try again.");

    private readonly string startKey = Guid.NewGuid().ToString("N");
    private StatementReviewDetail? detail;
    private StatementReview? review;
    private StatementNames? names;
    private ImmutableArray<StatementSelectableSpan> spans = [];
    private bool isDraftConflict;

    [Inject] private GetStatementReview GetReview { get; set; } = default!;
    [Inject] private CreateStatementDraft CreateDraft { get; set; } = default!;
    [Inject] private SaveStatementDraft SaveDraft { get; set; } = default!;
    [Inject] private DecideStatementReview DecideReview { get; set; } = default!;
    [Inject] private IssueConfiguration Issues { get; set; } = default!;

    [Parameter] public string CandidateId { get; set; } = string.Empty;

    [SupplyParameterFromForm(FormName = StartForm, Name = "idempotencyKey")] private string? StartKey { get; set; }
    [SupplyParameterFromForm(FormName = SaveForm)] private StatementInput? Input { get; set; }

    private StatementDraftRevision? Revision => review?.CurrentRevision;
    private bool IsAiRevision => Revision?.AuthorKind == ReviewAuthorKind.Analysis;
    private bool HasUnconfirmedAiDate => IsAiRevision && Revision!.StatementDate is not null;
    private string ArticleText => detail!.Extraction.Text;
    private ImmutableArray<AnalysisOfficial> Officials => detail?.Officials ?? [];
    private ImmutableArray<string> AllowedIssueIds => [.. detail!.Allowed.IssueIds.Order(StringComparer.Ordinal)];
    private ImmutableArray<string> AllowedPolicyAreaIds => [.. detail!.Allowed.PolicyAreaIds.Order(StringComparer.Ordinal)];
    private string Headline => Revision is null ? "Not drafted" : DraftStatusDisplay.DraftName(Revision.Headline);

    private IReadOnlyList<Crumb> Crumbs =>
        [new("Review", "/Review"), new("Statements", StatementRoutes.List), new(detail is not null ? Headline : notFound ? "Statement not found" : "Statement")];

    private IReadOnlyList<TabDefinition> StatementTabs =>
        [new("saved", $"Saved revision {Revision!.RevisionNumber}"), new("edit", "Edit wording")];

    protected override string? StatusText() =>
        Status == "started" ? "The draft is started. Write the wording, then save it." : base.StatusText();

    protected override Task LoadAsync() => LoadGuardedAsync(ReadRecordAsync);

    private async Task ReadRecordAsync()
    {
        detail = await GetReview.ExecuteAsync(actor, CandidateId, RequestAborted);
        if (detail is null)
        {
            ShowNotFound();
            return;
        }
        review = detail.Review;
        if (Revision is null) return;
        names = new StatementNames(detail.Officials, Issues);
        spans = StatementSelectableSpans.Build(detail.Extraction, Revision);
        Input ??= StatementInput.From(Revision);
    }

    protected override void ShowNotFound()
    {
        detail = null;
        review = null;
        base.ShowNotFound();
    }

    private async Task StartAsync() =>
        NavigateWhenSet(await RunCommandAsync(StartDraftAsync, StartFailures, LoadAsync));

    private Task<string> StartDraftAsync() =>
        StatementDraftStart.ExecuteAsync(CreateDraft, actor, CandidateId, StartKey, RequestAborted);

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
        if (SaveExpectedRevision != Revision!.RevisionNumber)
            throw new ReviewConflictException("The draft has a newer revision. Refresh before saving.");
        _ = await SaveDraft.ExecuteAsync(actor, BuildSaveRequest(Input ?? new StatementInput()), RequestAborted);
        return StatementRoutes.Editor(CandidateId, "saved");
    }

    private Task ReloadAfterSaveConflictAsync()
    {
        isDraftConflict = true;
        return LoadAsync();
    }

    private SaveStatementDraftRequest BuildSaveRequest(StatementInput input)
    {
        var date = ParseDate(input.StatementDate);
        var evidence = date is null ? null : FindSpan(input.DateEvidenceSelection)
            ?? throw new ArgumentException("Select the article passage that supports the statement date.");
        return new SaveStatementDraftRequest(CandidateId, SaveExpectedRevision, input.Headline ?? "", input.Summary ?? "", date, evidence,
            [.. input.SpeakerOfficialIds ?? []], OtherSpeakersOf(input), [.. input.IssueIds ?? []], [.. input.PolicyAreaIds ?? []],
            ResolveQuotations(input.QuotationSelection), SaveKey ?? "", input.StatementDateChecked == true);
    }

    private static DateOnly? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new ArgumentException("Enter the statement date in the requested format.");
        return date;
    }

    private static ImmutableArray<string> OtherSpeakersOf(StatementInput input) =>
        [.. (input.OtherSpeakersText ?? "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

    private ImmutableArray<SourceSpan> ResolveQuotations(string[]? keys)
    {
        var selections = keys ?? [];
        if (selections.Length > StatementDraftRevision.MaximumQuotations || selections.Distinct(StringComparer.Ordinal).Count() != selections.Length)
            throw new ArgumentException($"Select no more than {StatementDraftRevision.MaximumQuotations} distinct passages.");
        return [.. selections.Select(key => FindSpan(key)
            ?? throw new ArgumentException("A passage is no longer available. Refresh and choose it again."))];
    }

    private SourceSpan? FindSpan(string? key) =>
        spans.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.Ordinal))?.Span;

    private async Task DecideAsync() => NavigateWhenSet(await TryDecideAsync());

    private async Task<string?> TryDecideAsync()
    {
        if (detail is null) return null;
        return await RunCommandAsync(RecordDecisionAsync, DecideFailures, ReloadAfterDecideConflictAsync);
    }

    private async Task<string> RecordDecisionAsync()
    {
        var request = new DecideStatementReviewRequest(CandidateId, DecideExpectedRevision, DecideExpectedStateVersion,
            ParseDecisionKind(Kind), EmptyToNull(Decision?.Note), [.. Decision?.ResolvedDecisionIds ?? []], DecideKey ?? "");
        _ = await DecideReview.ExecuteAsync(actor, request, RequestAborted);
        return StatementRoutes.Editor(CandidateId, "decided");
    }

    private Task ReloadAfterDecideConflictAsync()
    {
        Input = null;
        return LoadAsync();
    }

    private string TextOf(SourceSpan span) => ArticleText.Substring(span.Start, span.Length);

    private static string SpanLabel(StatementSelectableSpan option) => option.Origin switch
    {
        StatementSpanOrigin.Saved => "Saved passage",
        StatementSpanOrigin.QuotedPassage => "Quoted passage",
        _ => "Paragraph"
    };

    private static string KindLabel(StatementQuotationKind kind) =>
        kind == StatementQuotationKind.DirectQuotation ? "Direct quotation" : "Release text";

    private StatementRecordContent SavedContent
    {
        get
        {
            var revision = Revision!;
            return new(Headline, revision.Summary, revision.StatementDate is { } date ? Moment.Iso(date) : "Not established",
                revision.StatementDateEvidence is { } evidence ? TextOf(evidence) : null,
                [.. revision.SpeakerOfficialIds.Select(names!.Official)], revision.OtherSpeakers,
                [.. revision.IssueIds.Select(names.Issue)], [.. revision.PolicyAreaIds.Select(names.PolicyArea)], revision.BillNumbers,
                [.. revision.Quotations.Select(quotation => new StatementQuoteContent(TextOf(quotation.Span), KindLabel(quotation.Kind)))]);
        }
    }

    private StatementRecordContent UnsavedContent
    {
        get
        {
            var input = Input!;
            var evidence = FindSpan(input.DateEvidenceSelection);
            return new(DraftStatusDisplay.DraftName(input.Headline ?? ""), input.Summary,
                string.IsNullOrWhiteSpace(input.StatementDate) ? "Not established" : input.StatementDate,
                string.IsNullOrWhiteSpace(input.StatementDate) || evidence is null ? null : TextOf(evidence),
                [.. (input.SpeakerOfficialIds ?? []).Select(names!.Official)], OtherSpeakersOf(input),
                [.. (input.IssueIds ?? []).Select(names.Issue)], [.. (input.PolicyAreaIds ?? []).Select(names.PolicyArea)], null,
                [.. spans.Where(option => (input.QuotationSelection ?? []).Contains(option.Key)).Select(option => new StatementQuoteContent(option.Text, null))]);
        }
    }

    private IReadOnlyList<TextRun> ArticleRuns => CitedText.Split(ArticleText, CitedRanges);

    private IReadOnlyList<CitedRange> CitedRanges
    {
        get
        {
            if (Revision is null) return [];
            List<CitedRange> ranges = [.. Revision.Quotations.Select((quotation, index) =>
                new CitedRange($"quotation-{index + 1}", quotation.Span.Start, quotation.Span.Length))];
            if (Revision.StatementDateEvidence is { } evidence) ranges.Add(new CitedRange("date-evidence", evidence.Start, evidence.Length));
            return ranges;
        }
    }

    private IReadOnlyList<(string Label, string Value)> TechnicalItems =>
    [
        ("Source", detail!.SourceId),
        ("URL", detail.Url),
        ("Candidate ID", detail.Review.CandidateId),
        ("Extraction ID", detail.Extraction.ExtractionId),
        .. detail.AnalysisRunId is null ? [] : new[] { ("AI analysis run", detail.AnalysisRunId) }
    ];

    private IReadOnlyList<ReviewHistoryEntry> History => ReviewHistoryEntry.Newest(
        review!.Revisions.Select(item => ReviewHistoryEntry.ForRevision(item.CreatedAtUtc, item.RevisionNumber, item.AuthorSubject, item.Headline)),
        review.Decisions);

    public sealed class StatementInput
    {
        public string? Headline { get; set; } = "";
        public string? Summary { get; set; } = "";
        public string? StatementDate { get; set; } = "";
        public string? DateEvidenceSelection { get; set; } = "";
        public bool? StatementDateChecked { get; set; }
        public string[]? SpeakerOfficialIds { get; set; } = [];
        public string? OtherSpeakersText { get; set; } = "";
        public string[]? IssueIds { get; set; } = [];
        public string[]? PolicyAreaIds { get; set; } = [];
        public string[]? QuotationSelection { get; set; } = [];

        internal static StatementInput From(StatementDraftRevision revision) => new()
        {
            Headline = revision.Headline,
            Summary = revision.Summary,
            StatementDate = revision.StatementDate is { } date ? Moment.Iso(date) : "",
            DateEvidenceSelection = revision.StatementDateEvidence is { } evidence ? StatementSelectableSpan.KeyOf(evidence) : "",
            SpeakerOfficialIds = [.. revision.SpeakerOfficialIds],
            OtherSpeakersText = string.Join('\n', revision.OtherSpeakers),
            IssueIds = [.. revision.IssueIds],
            PolicyAreaIds = [.. revision.PolicyAreaIds],
            QuotationSelection = [.. revision.Quotations.Select(quotation => StatementSelectableSpan.KeyOf(quotation.Span))]
        };
    }

}
