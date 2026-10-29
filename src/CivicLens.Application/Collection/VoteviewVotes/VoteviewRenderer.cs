using CivicLens.Application.Votes;
using static CivicLens.Application.Collection.RenderedText;

namespace CivicLens.Application.Collection.VoteviewVotes;

public static class VoteviewRenderer
{
    public static string Render(VoteviewRoll roll)
    {
        ArgumentNullException.ThrowIfNull(roll);
        var rollCall = roll.RollCall;
        var session = rollCall.Roll.Session;
        var lines = new List<string>
        {
            Line("Senate vote", string.Join(' ', Count(session.Congress), Count(session.Number), Count(rollCall.Roll.Number))),
            Line("Started", Date(rollCall.Date)),
            Line("Question", rollCall.Question)
        };
        AddLine(lines, "Vote title", rollCall.Title);
        lines.Add(Line("Result", rollCall.Result));
        AddLine(lines, "Majority required", rollCall.MajorityRequirement);
        lines.Add(Line("Count", CountsOf(roll)));
        lines.AddRange(roll.Votes.Select(MemberLineOf).Order(StringComparer.Ordinal));
        return string.Join('\n', lines);
    }

    public static string MemberLineOf(VoteviewMemberVote vote) =>
        MemberLine(vote.Member.BioguideId, vote.Member.Name, vote.Member.Party, vote.Member.State, vote.Cast);

    private static string CountsOf(VoteviewRoll roll) =>
        $"{VoteCountLabels.Yea} {Count(roll.Count(VoteviewCast.Yea))}, {VoteCountLabels.Nay} {Count(roll.Count(VoteviewCast.Nay))}, "
        + $"{VoteCountLabels.Present} {Count(roll.Count(VoteviewCast.Present))}, {VoteCountLabels.NotVoting} {Count(roll.Count(VoteviewCast.NotVoting))}";
}
