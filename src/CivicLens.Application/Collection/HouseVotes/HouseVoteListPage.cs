using CivicLens.Application.Collection.Votes;

namespace CivicLens.Application.Collection.HouseVotes;

public sealed record HouseVoteListPage(VoteSession Session, IReadOnlyList<HouseVoteListItem> Votes, string? Next);
