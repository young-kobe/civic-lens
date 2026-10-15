using System.Collections.Immutable;

namespace CivicLens.Application.Actions;

public sealed record ApproveSponsorshipChangesRequest(ImmutableArray<ActionApprovalTarget> Targets, string IdempotencyKey);
