namespace CivicLens.Application.Review;

public sealed class ReviewConflictException(string message) : InvalidOperationException(message);
