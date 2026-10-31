using CivicLens.Application.Issues;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Application.Votes;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Host.Components.Evidence;
using CivicLens.Host.Components.Layout;
using CivicLens.Host.Components.Ui;
using CivicLens.Host.Review;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;

namespace CivicLens.Host.Votes;

public sealed partial class VoteEditor
{
    private VoteReviewRead? detail;
    private bool isDraftConflict;

    [Inject] private GetVoteReview GetReview { get; set; } = default!;
    [Inject] private SaveVoteDraft SaveDraft { get; set; } = default!;
    [Inject] private DecideVoteReview DecideReview { get; set; } = default!;
    [Inject] private IssueConfiguration Issues { get; set; } = default!;

    [Parameter] public string RecordId { get; set; } = string.Empty;

    [SupplyParameterFromForm(FormName = SaveForm)] private VoteInput? Input { get; set; }

    private VoteDraftRevision Revision => detail!.Review.CurrentRevision;

    private IReadOnlyList<Crumb> Crumbs =>
        [new("Review", "/Review"), new(detail is not null ? Revision.Headline : notFound ? "Vote not found" : "Vote")];

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
        Input ??= VoteInput.From(Revision);
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
        _ = await SaveDraft.ExecuteAsync(actor, BuildSaveRequest(Input ?? new VoteInput()), RequestAborted);
        return VoteRoutes.Editor(RecordId, "saved");
    }

    private SaveVoteDraftRequest BuildSaveRequest(VoteInput input) =>
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
        var request = new DecideVoteReviewRequest(RecordId, DecideExpectedRevision, DecideExpectedStateVersion,
            ParseDecisionKind(Kind), EmptyToNull(Decision?.Note), [.. Decision?.ResolvedDecisionIds ?? []], DecideKey ?? "",
            CorrectionNoteOfApproval());
        _ = await DecideReview.ExecuteAsync(actor, request, RequestAborted);
        return VoteRoutes.Editor(RecordId, "decided");
    }

    private Task ReloadAfterDecideConflictAsync()
    {
        Input = null;
        return LoadAsync();
    }

    public override PublishedRecordReference TakeOffRecord => new(PublishedRecordKind.Vote, RecordId);

    private async Task TakeOffSiteAsync() =>
        NavigateWhenSet(detail is null ? null : await RunTakeOffSiteAsync(detail.Review.CurrentStatus, WithdrawAsync, ReloadAfterDecideConflictAsync));

    private Task WithdrawAsync(ReviewDecisionKind kind, string key) =>
        DecideReview.ExecuteAsync(actor, new DecideVoteReviewRequest(RecordId, TakeOffExpectedRevision, TakeOffExpectedStateVersion, kind,
            EmptyToNull(TakeOffNote), [], key), RequestAborted);

    private string Describe(MemberVote member) =>
        $"{detail!.PersonNames.GetValueOrDefault(member.PersonId, member.PersonId)}: {member.Cast}";

    private IReadOnlyList<Fact> FactsOf(VoteDraftRevision revision) =>
    [
        new("Roll call", VoteLabels.RollCall(revision)),
        new("Vote kind", VoteLabels.Kind(revision.Kind)),
        new("Bill", VoteLabels.Bill(revision)),
        Fact.List("Tracked issues", revision.TrackedIssueIds.Select(IssueName)),
        new("Evidence date", Moment.Day(revision.CandidateUpdateDate)),
        Fact.List("Member votes", revision.Members.Select(Describe))
    ];

    private string IssueName(string id) => Issues.Issues.FirstOrDefault(issue => issue.Id == id)?.Name ?? id;

    private IReadOnlyList<(string Label, string Value)> TechnicalItems =>
    [
        ("Record ID", RecordId),
        ("Candidate ID", Revision.CandidateId),
        ("Issue revision", Revision.IssueRevisionId)
    ];

    private IReadOnlyList<ReviewHistoryEntry> History => ReviewHistoryEntry.Newest(
        detail!.Review.Revisions.Select(item => ReviewHistoryEntry.ForRevision(item.CreatedAtUtc, item.RevisionNumber, item.AuthorSubject, item.Headline)),
        detail.Review.Decisions);

    public sealed class VoteInput
    {
        public string? Headline { get; set; } = "";
        public string? Summary { get; set; } = "";
        public string? SuggestionAction { get; set; }

        internal static VoteInput From(VoteDraftRevision revision) => new() { Headline = revision.Headline, Summary = revision.Summary };
    }
}
