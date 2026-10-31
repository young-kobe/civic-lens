using System.Collections.Immutable;
using CivicLens.Application.Actions;
using CivicLens.Application.Issues;
using CivicLens.Application.Review;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;
using CivicLens.Host.Components.Evidence;
using CivicLens.Host.Components.Layout;
using CivicLens.Host.Components.Ui;
using CivicLens.Host.Review;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;

namespace CivicLens.Host.Actions;

public sealed partial class ActionEditor
{
    private ActionReviewRead? detail;
    private bool isDraftConflict;

    [Inject] private GetActionReview GetReview { get; set; } = default!;
    [Inject] private SaveActionDraft SaveDraft { get; set; } = default!;
    [Inject] private DecideActionReview DecideReview { get; set; } = default!;
    [Inject] private IssueConfiguration Issues { get; set; } = default!;

    [Parameter] public string RecordId { get; set; } = string.Empty;

    [SupplyParameterFromForm(FormName = SaveForm)] private ActionInput? Input { get; set; }

    private ActionDraftRevision Revision => detail!.Review.CurrentRevision;

    private IReadOnlyList<Crumb> Crumbs =>
        [new("Review", "/Review"), new(detail is not null ? Revision.Headline : notFound ? "Action not found" : "Action")];

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
        Input ??= ActionInput.From(Revision);
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
            throw new ReviewConflictException("The draft has a newer revision. Refresh before saving.");
        _ = await SaveDraft.ExecuteAsync(actor, BuildSaveRequest(Input ?? new ActionInput()), RequestAborted);
        return ActionRoutes.Editor(RecordId, "saved");
    }

    private SaveActionDraftRequest BuildSaveRequest(ActionInput input) =>
        new(RecordId, SaveExpectedRevision, input.Headline ?? "", input.Summary ?? "",
            SuggestionActions.ChoiceOf(input.SuggestionAction), SaveKey ?? "");

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
        var request = new DecideActionReviewRequest(RecordId, DecideExpectedRevision, DecideExpectedStateVersion,
            ParseDecisionKind(Kind), EmptyToNull(Decision?.Note), [.. Decision?.ResolvedDecisionIds ?? []], DecideKey ?? "",
            CorrectionNoteOfApproval());
        _ = await DecideReview.ExecuteAsync(actor, request, RequestAborted);
        return ActionRoutes.Editor(RecordId, "decided");
    }

    private Task ReloadAfterDecideConflictAsync()
    {
        Input = null;
        return LoadAsync();
    }

    private async Task TakeOffSiteAsync() => NavigateWhenSet(detail is null ? null : await RunTakeOffSiteAsync(
        PublishedRecordKind.Action, RecordId, detail.Review.CurrentStatus, WithdrawAsync, ActionRoutes.Editor(RecordId, TakeOffSitePanel.DoneStatus),
        ReloadAfterDecideConflictAsync));

    private Task WithdrawAsync(ReviewDecisionKind kind, string key) =>
        DecideReview.ExecuteAsync(actor, new DecideActionReviewRequest(RecordId, TakeOffExpectedRevision, TakeOffExpectedStateVersion, kind,
            EmptyToNull(TakeOffNote), [], key), RequestAborted);

    private string Describe(ActionSponsorship sponsorship)
    {
        var name = detail!.OfficialNames.GetValueOrDefault(sponsorship.PersonId, sponsorship.PersonId);
        var role = sponsorship.Role == ActionSponsorshipRole.Sponsor ? "Sponsor"
            : sponsorship.IsOriginal ? "Original cosponsor" : "Cosponsor";
        var withdrawn = sponsorship.WithdrawnOn is { } date ? $", withdrew {Moment.Iso(date)}" : "";
        return $"{name}: {role} from {Moment.Iso(sponsorship.SponsoredOn)}{withdrawn}";
    }

    private IReadOnlyList<Fact> FactsOf(ActionDraftRevision revision) =>
    [
        new("Bill", ActionLabels.Bill(revision)),
        new("Title", revision.BillTitle),
        new("Policy area", revision.PolicyArea ?? "None"),
        Fact.List("Tracked issues", revision.TrackedIssueIds.Select(IssueName)),
        new("Evidence date", Moment.Day(revision.CandidateUpdateDate)),
        new("Sponsorships", revision.Sponsorships.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))
    ];

    private string IssueName(string id) => Issues.Issues.FirstOrDefault(issue => issue.Id == id)?.Name ?? id;

    private IReadOnlyList<(string Label, string Value)> TechnicalItems =>
    [
        ("Record ID", RecordId),
        ("Candidate ID", Revision.CandidateId),
        ("Fingerprint", Revision.Fingerprint),
        .. Revision.SummaryVersion is { } version ? new[] { ("Summary version", version) } : []
    ];

    private IReadOnlyList<ReviewHistoryEntry> History => ReviewHistoryEntry.Newest(
        detail!.Review.Revisions.Select(item => ReviewHistoryEntry.ForRevision(item.CreatedAtUtc, item.RevisionNumber, item.AuthorSubject, item.Headline)),
        detail.Review.Decisions);

    public sealed class ActionInput
    {
        public string? Headline { get; set; } = "";
        public string? Summary { get; set; } = "";
        public string? SuggestionAction { get; set; }

        internal static ActionInput From(ActionDraftRevision revision) => new() { Headline = revision.Headline, Summary = revision.Summary };
    }
}
