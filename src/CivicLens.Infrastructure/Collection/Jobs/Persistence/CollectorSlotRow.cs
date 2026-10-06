namespace CivicLens.Infrastructure.Collection.Jobs.Persistence;

internal sealed class CollectorSlotRow
{
    public int Id { get; set; } = 1;
    public string? JobId { get; set; }
    public string? AttemptId { get; set; }
    public string? Token { get; set; }
    public long Fence { get; set; }
    public long? ExpiresAt { get; set; }
}
