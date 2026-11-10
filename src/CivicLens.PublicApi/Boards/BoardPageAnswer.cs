namespace CivicLens.PublicApi.Boards;

public sealed record BoardPageAnswer(BoardIdentity Board, int? Congress, int? NewerCongress, int? WithoutVotes,
    int? NotInCongress, int? WithoutMajorParty, BoardMemberAnswer[]? Members, string[]? Breakers,
    string[]? Loyalists, string[]? MostMissed, string[]? TooFewSplitVotes);
