namespace CivicLens.Application.Collection.VoteviewVotes;

public sealed record VoteviewRoll(VoteviewRollCall RollCall, IReadOnlyList<VoteviewMemberVote> Votes)
{
    public static IReadOnlyList<VoteviewRoll> Join(IReadOnlyList<VoteviewRollCall> rollCalls, IReadOnlyList<VoteviewCast> casts,
        VoteviewMemberFile members)
    {
        var castsByPart = casts.Where(cast => !members.PresidentIcpsrs.Contains(cast.Icpsr)).ToLookup(cast => cast.Part);
        return [.. rollCalls.Select(rollCall => new VoteviewRoll(rollCall, VotesOf(castsByPart[rollCall.Part], members.Senators)))
            .Where(roll => roll.CountsAgree())];
    }

    public int Count(string cast) => Votes.Count(vote => vote.Cast == cast);

    private bool CountsAgree() => Count(VoteviewCast.Yea) == RollCall.YeaCount && Count(VoteviewCast.Nay) == RollCall.NayCount;

    private static VoteviewMemberVote[] VotesOf(IEnumerable<VoteviewCast> casts, IReadOnlyDictionary<int, VoteviewMember> members)
    {
        var votes = casts.Select(cast => new VoteviewMemberVote(
            members.GetValueOrDefault(cast.Icpsr) ?? throw new InvalidDataException("Voteview vote file names a member that is not in the member file."),
            cast.Cast)).ToArray();
        if (votes.Select(vote => vote.Member.Icpsr).Distinct().Count() != votes.Length)
            throw new InvalidDataException("Voteview vote file lists a member twice in one roll call.");
        return votes;
    }
}

public sealed record VoteviewMemberVote(VoteviewMember Member, string Cast);
