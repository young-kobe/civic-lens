using CivicLens.Application.Issues;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public sealed record VoteDraftContext(VoteDraftEvidence Evidence, VoteSnapshot Snapshot, BillIssueChoice Issues,
    string IssueRevisionId)
{
    public VoteChange ChangeSinceCurrent()
    {
        var change = ChangeFromFacts(Evidence.Current);
        if (!Evidence.HasStaleDraft) return change;
        return change == VoteChange.None ? VoteChange.StaleOnly : VoteChange.Other;
    }

    private VoteChange ChangeFromFacts(VoteDraftRevision? previous)
    {
        if (previous is null || previous.Source != Snapshot.Source || !previous.Facts.Select(fact => fact.Text).SequenceEqual(Snapshot.Facts.Select(fact => fact.Text)) ||
            previous.IssueCandidateIds.IsDefault || !previous.IssueCandidateIds.SequenceEqual(Issues.CandidateIds) || previous.IssueRevisionId != IssueRevisionId ||
            previous.SubjectsHash != Issues.SubjectsHash || previous.Decides?.SourceHash != Snapshot.Decides?.SourceHash)
            return VoteChange.Other;
        if (!previous.Members.Select(VoteOf).SequenceEqual(Snapshot.Members.Select(VoteOf))) return VoteChange.MemberVotesOnly;
        return previous.Kind == Snapshot.Kind ? VoteChange.None : VoteChange.KindOnly;
    }

    private static (string PersonId, string BioguideId, string Cast) VoteOf(MemberVote member) =>
        (member.PersonId, member.BioguideId, member.Cast);
}

public enum VoteChange { None, MemberVotesOnly, KindOnly, StaleOnly, Other }
