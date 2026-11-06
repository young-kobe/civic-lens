using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

public sealed record CastGroup(string Lead, string Word, string Cast, string Count, PublishedMemberVote[] Members);
