using System.Collections.Immutable;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Review;

public sealed record ReviewTriagePage<TReview>(ImmutableArray<ReviewTriageRow<TReview>> Rows,
    ImmutableDictionary<string, DocumentExtraction> Extractions, string? NewerCursor, string? OlderCursor);
