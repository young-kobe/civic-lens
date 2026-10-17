using System.Collections.Immutable;

namespace CivicLens.Application.Statements;

public sealed record SaveGateLabelRequest(string CandidateId, string ExtractionId, ImmutableArray<string> IssueIds, bool NamesBillOrVote);
