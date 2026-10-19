using System.Collections.Immutable;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Review;

public sealed record ReviewTriageRead<TReview>(ImmutableArray<TReview> Items,
    ImmutableDictionary<string, DocumentExtraction> Extractions, ImmutableDictionary<string, int> Priorities, string? NewerCursor, string? OlderCursor);
