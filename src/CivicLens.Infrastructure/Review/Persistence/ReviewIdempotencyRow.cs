namespace CivicLens.Infrastructure.Review.Persistence;

internal sealed class ReviewIdempotencyRow
{
    public string ActorSubject { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
}
