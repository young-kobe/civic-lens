using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal sealed record PartyBreakers(string Heading, string None, PublishedMemberVote[] Members);
