namespace CivicLens.Core.Review;

public sealed class ReviewCycleFullException() : ArgumentException(
    "This record holds the most decisions that one review cycle can hold. Approve the record to start a new cycle, then add another decision.");
