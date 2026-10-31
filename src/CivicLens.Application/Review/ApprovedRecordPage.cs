using System.Collections.Immutable;

namespace CivicLens.Application.Review;

public sealed record ApprovedRecordPage(ImmutableArray<ApprovedRecordHit> Items, string? NewerCursor, string? OlderCursor);
