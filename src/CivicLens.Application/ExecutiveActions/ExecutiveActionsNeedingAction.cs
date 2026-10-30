using System.Collections.Immutable;
using CivicLens.Core.ExecutiveActions;

namespace CivicLens.Application.ExecutiveActions;

public sealed record ExecutiveActionsNeedingAction(ImmutableArray<ExecutiveActionReview> Items, int Total);
