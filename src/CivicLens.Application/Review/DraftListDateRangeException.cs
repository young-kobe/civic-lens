namespace CivicLens.Application.Review;

public sealed class DraftListDateRangeException() : ArgumentException("The start date must not be after the end date.");
