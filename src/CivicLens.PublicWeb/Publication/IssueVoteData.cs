using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

public sealed record IssueVoteData(string RecordId, DateOnly VotedOn, PublishedChamber Chamber, string Headline, string? Label, PublishedTextOrigin TextOrigin, string ResultWords, int? Yea, int? Nay);
