namespace CivicLens.Application.Review;

public sealed record CreateDocumentChangeDraftRequest(string ComparisonId, string IdempotencyKey);
