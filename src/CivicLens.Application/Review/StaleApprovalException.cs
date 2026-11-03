namespace CivicLens.Application.Review;

/// <summary>An approval that a person cannot give yet, because the record is behind its source. The message holds the steps that fix it.</summary>
public sealed class StaleApprovalException(string message) : ArgumentException(message);
