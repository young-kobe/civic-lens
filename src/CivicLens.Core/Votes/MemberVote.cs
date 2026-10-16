using CivicLens.Core.Documents;

namespace CivicLens.Core.Votes;

public sealed record MemberVote(string PersonId, string BioguideId, string Cast, SourceSpan Citation);
