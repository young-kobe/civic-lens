using CivicLens.Application.Issues;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;

namespace CivicLens.Application.Actions;

public sealed record ActionDraftContext(ActionDraftEvidence Evidence, ActionBillSnapshot Snapshot, BillIssueChoice Issues,
    string? PolicyAreaId, string IssueRevisionId)
{
    public bool CanBeWrittenByCode => Snapshot.Bill.SummaryCount == 0 && Issues.CandidateIds.IsEmpty &&
        Evidence.Current is not { TextOrigin: ReviewTextOrigin.Person };

    public ActionChange ChangeSinceCurrent()
    {
        if (Evidence.Current is not { } previous || ClaimsNoSummaryFalsely(previous)) return ActionChange.Other;
        var change = ChangeFrom(previous);
        return change == ActionChange.None && Evidence.HasOlderPromptDraft ? ActionChange.Other : change;
    }

    private bool ClaimsNoSummaryFalsely(ActionDraftRevision previous) =>
        previous.TextOrigin == ReviewTextOrigin.Code && Snapshot.Bill.SummaryCount > 0;

    private ActionChange ChangeFrom(ActionDraftRevision previous)
    {
        if (previous.SummaryVersion != Snapshot.SummaryVersion || previous.PolicyArea != Snapshot.Bill.PolicyArea || previous.PolicyAreaId != PolicyAreaId ||
            previous.BillTitle != Snapshot.Bill.Title || previous.IssueCandidateIds.IsDefault || !previous.IssueCandidateIds.SequenceEqual(Issues.CandidateIds) ||
            previous.IssueRevisionId != IssueRevisionId ||
            Evidence.Candidate.IncludesSubjects && previous.SubjectsHash != Issues.SubjectsHash)
            return ActionChange.Other;
        if (previous.Fingerprint != Snapshot.Fingerprint)
            return previous.HasSameSponsorsAs(Snapshot.Sponsorships) ? ActionChange.SponsorshipsOnly : ActionChange.Other;
        return previous.Sponsorships.Select(item => item.PersonId).SequenceEqual(Snapshot.Sponsorships.Select(item => item.PersonId))
            ? ActionChange.None : ActionChange.Other;
    }
}

public enum ActionChange { None, SponsorshipsOnly, Other }
