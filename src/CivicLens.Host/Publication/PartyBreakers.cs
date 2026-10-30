using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal sealed record PartyBreakers(string Heading, string Casts, string None, PublishedMemberVote[] Members);
