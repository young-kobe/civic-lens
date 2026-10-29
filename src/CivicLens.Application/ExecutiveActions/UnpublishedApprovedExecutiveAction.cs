using CivicLens.Core.ExecutiveActions;

namespace CivicLens.Application.ExecutiveActions;

public sealed record UnpublishedApprovedExecutiveAction(ExecutiveActionReview Action, int? PublishedRevisionNumber);
