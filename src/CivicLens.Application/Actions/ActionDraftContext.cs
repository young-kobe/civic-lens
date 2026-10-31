using CivicLens.Application.Issues;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;

namespace CivicLens.Application.Actions;

public sealed record ActionDraftContext(ActionDraftEvidence Evidence, ActionBillSnapshot Snapshot, BillIssueChoice Issues,
    string? PolicyAreaId, string IssueRevisionId)
{
    /// <summary>Congress.gov counts a summary, but this candidate holds none that the model can read.</summary>
    private bool SummaryIsUnreadable => Snapshot.Bill.SummaryCount > 0 && Snapshot.NewestSummary is null;

    /// <summary>Text that a person wrote stays. Model text stays while a summary exists that this candidate cannot offer.</summary>
    public bool KeepsCurrentText => Evidence.Current is { } current && !SaysNoSummaryFalsely(current) &&
        (current.TextOrigin == ReviewTextOrigin.Person || current.TextOrigin == ReviewTextOrigin.Model && Snapshot.Bill.SummaryCount > 0);

    public ActionChange ChangeSinceCurrent()
    {
        if (Evidence.Current is not { } previous || SaysNoSummaryFalsely(previous)) return ActionChange.Other;
        var change = ChangeFrom(previous);
        return change == ActionChange.None && Evidence.HasOlderPromptDraft ? ActionChange.OlderPromptOnly : change;
    }

    private bool SaysNoSummaryFalsely(ActionDraftRevision revision) => Snapshot.Bill.SummaryCount > 0 &&
        revision.TextOrigin != ReviewTextOrigin.Person && revision.Summary.Contains(ActionCodeText.NoSummarySentence, StringComparison.Ordinal);

    private ActionChange ChangeFrom(ActionDraftRevision previous)
    {
        if (!SummaryIsUnreadable && previous.SummaryVersion != Snapshot.SummaryVersion || previous.PolicyArea != Snapshot.Bill.PolicyArea ||
            previous.PolicyAreaId != PolicyAreaId || previous.BillTitle != Snapshot.Bill.Title || previous.IssueCandidateIds.IsDefault ||
            !previous.IssueCandidateIds.SequenceEqual(Issues.CandidateIds) || previous.IssueRevisionId != IssueRevisionId ||
            Evidence.Candidate.IncludesSubjects && previous.SubjectsHash != Issues.SubjectsHash)
            return ActionChange.Other;
        if (previous.Fingerprint != Snapshot.Fingerprint)
            return previous.HasSameSponsorsAs(Snapshot.Sponsorships) ? ActionChange.SponsorshipsOnly : ActionChange.Other;
        return previous.Sponsorships.Select(item => item.PersonId).SequenceEqual(Snapshot.Sponsorships.Select(item => item.PersonId))
            ? ActionChange.None : ActionChange.Other;
    }
}

public enum ActionChange { None, SponsorshipsOnly, OlderPromptOnly, Other }
