using CivicLens.Core.Actions;

namespace CivicLens.Application.Actions;

public sealed record UnpublishedApprovedAction(ActionReview Action, int? PublishedRevisionNumber);
