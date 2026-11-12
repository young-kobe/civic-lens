namespace CivicLens.PublicApi.Boards;

public sealed record BoardMemberAnswer(string OfficialId, string Name, BoardTermAnswer[] Terms, int Cast, int Breaks,
    BoardMissedVotesAnswer? MissedVotes, string PagePath, string PartyLineScope);
