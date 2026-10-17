using CivicLens.Core.Review;

namespace CivicLens.Core.Statements;

public sealed record GateLabelEntry(string CandidateId, string ExtractionId, string AuthorSubject, ReviewAuthorKind AuthorKind,
    string? RunId, GateLabel Label, DateTimeOffset CreatedAtUtc);
