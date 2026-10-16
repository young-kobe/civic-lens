using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public sealed record VoteDraftContext(VoteDraftEvidence Evidence, VoteSnapshot Snapshot)
{
    public VoteChange ChangeSinceCurrent()
    {
        var previous = Evidence.Current;
        if (previous is null || !previous.Facts.Select(fact => fact.Text).SequenceEqual(Snapshot.Facts.Select(fact => fact.Text)) ||
            !previous.Members.Select(IdentityOf).SequenceEqual(Snapshot.Members.Select(IdentityOf)))
            return VoteChange.Other;
        return previous.Members.Select(member => member.Cast).SequenceEqual(Snapshot.Members.Select(member => member.Cast))
            ? VoteChange.None : VoteChange.MemberVotesOnly;
    }

    private static (string PersonId, string BioguideId) IdentityOf(MemberVote member) => (member.PersonId, member.BioguideId);
}

public enum VoteChange { None, MemberVotesOnly, Other }
