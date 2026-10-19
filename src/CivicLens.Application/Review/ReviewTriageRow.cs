namespace CivicLens.Application.Review;

public sealed record ReviewTriageRow<TReview>(TReview Review, ApprovalCheck Check, int? Priority = null);
