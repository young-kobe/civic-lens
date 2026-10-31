using CivicLens.Application.Relationships;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Application.Statements;
using CivicLens.Core.Documents;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;
using CivicLens.Host.Components.Evidence;
using CivicLens.Host.Components.Layout;
using CivicLens.Host.Components.Ui;
using CivicLens.Host.Review;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;

namespace CivicLens.Host.Relationships;

public sealed partial class RelationshipEditor
{
    private RelationshipReviewRead? detail;
    private bool isDraftConflict;

    [Inject] private GetRelationshipReview GetReview { get; set; } = default!;
    [Inject] private SaveRelationshipDraft SaveDraft { get; set; } = default!;
    [Inject] private DecideRelationshipReview DecideReview { get; set; } = default!;

    [Parameter] public string RecordId { get; set; } = string.Empty;

    [SupplyParameterFromForm(FormName = SaveForm)] private RelationshipInput? Input { get; set; }

    private RelationshipRevision Revision => detail!.Review.CurrentRevision;

    private string TargetHeadline => detail!.CitedTarget.Headline;

    private IReadOnlyList<Crumb> Crumbs =>
        [new("Review", "/Review"), new("Relationships", RelationshipRoutes.List),
            new(detail is not null ? RelationshipLabels.Type(Revision.Type) : notFound ? "Relationship not found" : "Relationship")];

    private IReadOnlyList<TabDefinition> RecordTabs =>
        [new("saved", $"Saved revision {Revision.RevisionNumber}"), new("edit", "Edit link")];

    protected override Task LoadAsync() => LoadGuardedAsync(ReadRecordAsync);

    private async Task ReadRecordAsync()
    {
        detail = await GetReview.ExecuteAsync(actor, RecordId, RequestAborted);
        if (detail is null)
        {
            ShowNotFound();
            return;
        }
        Input ??= RelationshipInput.From(Revision);
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
            throw new ReviewConflictException("The relationship has a newer revision. Refresh before saving.");
        _ = await SaveDraft.ExecuteAsync(actor, BuildSaveRequest(Input ?? new RelationshipInput()), RequestAborted);
        return RelationshipRoutes.Editor(RecordId, "saved");
    }

    private SaveRelationshipDraftRequest BuildSaveRequest(RelationshipInput input) =>
        new(RecordId, SaveExpectedRevision, RelationshipTypes.Parse(input.Type, Revision.Target.Kind)
            ?? throw new ArgumentException("Choose a type that fits the target.", nameof(input)), input.Reason ?? "",
            QuoteOf(input.QuoteSelection) ?? throw new ArgumentException("Choose one of the offered passages.", nameof(input)),
            input.FactId ?? "", SaveKey ?? "");

    private SourceSpan? QuoteOf(string? key) =>
        detail!.Options.Quotes.FirstOrDefault(quote => StatementSelectableSpan.KeyOf(quote.Span) == key)?.Span;

    private Task ReloadAfterSaveConflictAsync()
    {
        isDraftConflict = true;
        return LoadAsync();
    }

    private async Task DecideAsync() => NavigateWhenSet(await TryDecideAsync());

    private async Task<string?> TryDecideAsync()
    {
        if (detail is null) return null;
        return await RunCommandAsync(RecordDecisionAsync, DecideFailures, ReloadAfterDecideConflictAsync);
    }

    private async Task<string> RecordDecisionAsync()
    {
        var request = new DecideRelationshipReviewRequest(RecordId, DecideExpectedRevision, DecideExpectedStateVersion,
            ParseDecisionKind(Kind), EmptyToNull(Decision?.Note), [.. Decision?.ResolvedDecisionIds ?? []], DecideKey ?? "",
            CorrectionNoteOfApproval());
        _ = await DecideReview.ExecuteAsync(actor, request, RequestAborted);
        return RelationshipRoutes.Editor(RecordId, "decided");
    }

    private Task ReloadAfterDecideConflictAsync()
    {
        Input = null;
        return LoadAsync();
    }

    public override PublishedRecordReference TakeOffRecord => new(PublishedRecordKind.Relationship, RecordId);

    private async Task TakeOffSiteAsync() =>
        NavigateWhenSet(detail is null ? null : await RunTakeOffSiteAsync(detail.Review.CurrentStatus, WithdrawAsync, ReloadAfterDecideConflictAsync));

    private Task WithdrawAsync(ReviewDecisionKind kind, string key) =>
        DecideReview.ExecuteAsync(actor, new DecideRelationshipReviewRequest(RecordId, TakeOffExpectedRevision, TakeOffExpectedStateVersion, kind,
            EmptyToNull(TakeOffNote), [], key), RequestAborted);

    private string QuoteText(SourceSpan span) => detail!.CitedStatement.ArticleText.Substring(span.Start, span.Length);

    private IReadOnlyList<Fact> SavedFacts(RelationshipRevision revision) =>
    [
        new("Type", RelationshipLabels.Type(revision.Type)),
        new("Quote", QuoteText(revision.StatementQuote)),
        new("Fact", detail!.CitedTarget.Facts.Single(fact => fact.Id == revision.TargetFactId).Text)
    ];

    private IReadOnlyList<Fact> InputFacts(RelationshipInput input) =>
    [
        new("Type", RelationshipTypes.Parse(input.Type, Revision.Target.Kind) is { } type ? RelationshipLabels.Type(type) : "None chosen"),
        new("Reason", input.Reason ?? ""),
        new("Quote", QuoteOf(input.QuoteSelection) is { } quote ? QuoteText(quote) : "None chosen"),
        new("Fact", detail!.CitedTarget.Facts.FirstOrDefault(fact => fact.Id == input.FactId)?.Text ?? "None chosen")
    ];

    private IReadOnlyList<Fact> StatementFacts(RelationshipRevision revision) =>
    [
        new("Cited revision", revision.Statement.RevisionNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new("Statement date", detail!.CitedStatement.Revision.StatementDate is { } date ? Moment.Iso(date) : "Not stated"),
        new("Match basis", RelationshipLabels.Basis(revision.Match.Basis)),
        new("Matched words", QuoteText(revision.Match.Span))
    ];

    private IReadOnlyList<Fact> TargetFacts(RelationshipRevision revision) =>
    [
        new("Cited revision", revision.Target.RevisionNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        .. detail!.CitedTarget.Facts.Select(fact => new Fact(fact.Id, fact.Text))
    ];

    private IReadOnlyList<ReviewHistoryEntry> History => ReviewHistoryEntry.Newest(
        detail!.Review.Revisions.Select(item => ReviewHistoryEntry.ForRevision(item.CreatedAtUtc, item.RevisionNumber, item.AuthorSubject,
            RelationshipLabels.Summary(item))),
        detail.Review.Decisions);

    public sealed class RelationshipInput
    {
        public string? Type { get; set; }
        public string? Reason { get; set; } = "";
        public string? QuoteSelection { get; set; }
        public string? FactId { get; set; }

        internal static RelationshipInput From(RelationshipRevision revision) => new()
        {
            Type = RelationshipTypes.WireName(revision.Type),
            Reason = revision.Reason,
            QuoteSelection = StatementSelectableSpan.KeyOf(revision.StatementQuote),
            FactId = revision.TargetFactId
        };
    }
}
