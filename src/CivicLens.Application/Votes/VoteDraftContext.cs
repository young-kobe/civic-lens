using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public sealed record VoteDraftContext(VoteDraftEvidence Evidence, VoteSnapshot Snapshot)
{
    public VoteChange ChangeSinceCurrent()
    {
        var previous = Evidence.Current;
        if (previous is null || !previous.Facts.Select(fact => fact.Text).SequenceEqual(Snapshot.Facts.Select(fact => fact.Text)))
            return VoteChange.Other;
        return previous.Members.Select(VoteOf).SequenceEqual(Snapshot.Members.Select(VoteOf)) ? VoteChange.None : VoteChange.MemberVotesOnly;
    }

    private static (string PersonId, string BioguideId, string Cast) VoteOf(MemberVote member) =>
        (member.PersonId, member.BioguideId, member.Cast);
}

public enum VoteChange { None, MemberVotesOnly, Other }
