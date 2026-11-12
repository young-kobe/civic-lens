namespace CivicLens.Application.Publication;

public sealed record VotePartyTallyPage(IReadOnlyList<VotePartyTallyEvidence> Revisions, bool HasMore);
