using System.Collections.Immutable;
using CivicLens.Core.Actions;

namespace CivicLens.Application.Actions;

public sealed record ActionsNeedingAction(ImmutableArray<ActionReview> Items, int Total);
