namespace CivicLens.Infrastructure.Collection.Jobs.Persistence;

internal sealed class CollectorSlotRow
{
    public string PacingKey { get; set; } = "";
    public string? JobId { get; set; }
    public string? AttemptId { get; set; }
    public string? Token { get; set; }
    public long Fence { get; set; }
    public long? ExpiresAt { get; set; }
}
