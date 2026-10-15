using System.Collections.Immutable;
using CivicLens.Core.Actions;

namespace CivicLens.Application.Actions;

public sealed record ActionDraftContext(ActionDraftEvidence Evidence, ActionBillSnapshot Snapshot, ImmutableArray<string> TrackedIssueIds,
    string? PolicyAreaId, string IssueRevisionId)
{
    public ActionChange ChangeSince(ActionDraftRevision? previous)
    {
        if (previous is null || previous.SummaryVersion != Snapshot.SummaryVersion || previous.PolicyArea != Snapshot.Bill.PolicyArea || previous.PolicyAreaId != PolicyAreaId ||
            previous.BillTitle != Snapshot.Bill.Title || !previous.TrackedIssueIds.SequenceEqual(TrackedIssueIds) ||
            previous.IssueRevisionId != IssueRevisionId)
            return ActionChange.Other;
        if (previous.Fingerprint != Snapshot.Fingerprint)
            return previous.HasSameSponsorsAs(Snapshot.Sponsorships) ? ActionChange.SponsorshipsOnly : ActionChange.Other;
        return previous.Sponsorships.Select(item => item.PersonId).SequenceEqual(Snapshot.Sponsorships.Select(item => item.PersonId))
            ? ActionChange.None : ActionChange.Other;
    }
}

public enum ActionChange { None, SponsorshipsOnly, Other }
