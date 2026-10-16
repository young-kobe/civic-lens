namespace CivicLens.Application.Collection.HouseVotes;

public sealed record HouseVoteListPage(HouseVoteSession Session, IReadOnlyList<HouseVoteListItem> Votes, string? Next);
