using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal sealed record CastGroup(string Lead, string Word, string Cast, string Count, PublishedMemberVote[] Members);
