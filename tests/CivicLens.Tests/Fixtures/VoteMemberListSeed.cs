using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Fixtures;

internal static class VoteMemberListSeed
{
    public const string Columns = "member_count, yea_count, nay_count, present_count, not_voting_count, other_count";

    public static (short Total, short Yea, short Nay, short Present, short NotVoting, short Other) Counts(IEnumerable<string> casts)
    {
        var groups = casts.ToLookup(cast => PublishedOfficialVote.SideOf(cast) ?? cast, StringComparer.Ordinal);
        var total = groups.Sum(group => group.Count());
        var yea = groups[PublishedPartyMajority.Yea].Count();
        var nay = groups[PublishedPartyMajority.Nay].Count();
        var present = groups[PublishedOfficialVote.Present].Count();
        var notVoting = groups[PublishedOfficialVote.NotVoting].Count();
        return (checked((short)total), checked((short)yea), checked((short)nay), checked((short)present),
            checked((short)notVoting), checked((short)(total - yea - nay - present - notVoting)));
    }

    public static string Values(IEnumerable<string> casts)
    {
        var counts = Counts(casts);
        return FormattableString.Invariant($"{counts.Total}, {counts.Yea}, {counts.Nay}, {counts.Present}, {counts.NotVoting}, {counts.Other}");
    }
}
