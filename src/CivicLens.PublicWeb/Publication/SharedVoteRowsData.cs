using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

public sealed record SharedVoteRowsData(PublishedChamber Chamber, SharedVoteMemberData[] Members, SharedVoteRowData[] Votes);
