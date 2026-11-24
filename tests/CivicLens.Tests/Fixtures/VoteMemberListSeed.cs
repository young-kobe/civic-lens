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
    // These fixture casts represent the whole source, unless a test overrides the frozen totals explicitly.
    public static string WholeTotalsUpdate(string record, int revision, IEnumerable<string> casts)
    {
        var counts = Counts(casts);
        var totals = counts.Other == 0
            ? FormattableString.Invariant($"{counts.Yea}, total_nay = {counts.Nay}, total_present = {counts.Present}, total_not_voting = {counts.NotVoting}")
            : "NULL, total_nay = NULL, total_present = NULL, total_not_voting = NULL";
        return FormattableString.Invariant($"UPDATE vote_revisions SET totals_written = true, total_yea = {totals} WHERE record_id = '{record}' AND revision_number = {revision};");
    }
}
