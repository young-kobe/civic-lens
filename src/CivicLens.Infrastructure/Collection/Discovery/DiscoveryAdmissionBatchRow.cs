namespace CivicLens.Infrastructure.Collection.Discovery;

internal sealed class DiscoveryAdmissionBatchRow
{
    public string IdempotencyKey { get; set; } = null!;
    public string AttemptId { get; set; } = null!;
    public string InputJson { get; set; } = null!;
    public string ResultJson { get; set; } = null!;
    public long CreatedAt { get; set; }
}
