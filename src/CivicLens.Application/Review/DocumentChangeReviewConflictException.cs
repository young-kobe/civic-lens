namespace CivicLens.Application.Review;

public sealed class DocumentChangeReviewConflictException(string message) : InvalidOperationException(message);
