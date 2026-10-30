using CivicLens.Application.Issues;

namespace CivicLens.Application.Actions;

public sealed record ActionDraftContext(ActionDraftEvidence Evidence, ActionBillSnapshot Snapshot, BillIssueChoice Issues,
    string? PolicyAreaId, string IssueRevisionId)
{
    public ActionChange ChangeSinceCurrent()
    {
        var previous = Evidence.Current;
        if (previous is null || previous.SummaryVersion != Snapshot.SummaryVersion || previous.PolicyArea != Snapshot.Bill.PolicyArea || previous.PolicyAreaId != PolicyAreaId ||
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
