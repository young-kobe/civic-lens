using System.Collections.Immutable;

namespace CivicLens.Application.Review;

public sealed record NewDocumentChangePage(ImmutableArray<NewDocumentChange> Items,
    string? NewerCursor, string? OlderCursor);
