using System.Collections.Immutable;

namespace CivicLens.Application.Review;

public sealed record BulkApprovalRequest(ImmutableArray<BulkApprovalTarget> Targets, string IdempotencyKey);
