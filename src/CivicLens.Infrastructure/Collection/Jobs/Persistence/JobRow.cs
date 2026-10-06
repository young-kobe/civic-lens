using CivicLens.Application.Collection.Jobs;

namespace CivicLens.Infrastructure.Collection.Jobs.Persistence;

internal sealed class JobRow
{
    public string JobId { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string DefinitionJson { get; set; } = "";
    public CollectionJobState State { get; set; }
    public long CreatedAt { get; set; }
    public long? RetryAt { get; set; }
    public bool CancellationRequested { get; set; }
    public int ChargedRequests { get; set; }
    public long ChargedBytes { get; set; }
    public int ChargedSeconds { get; set; }
    public string? LeaseToken { get; set; }
    public long LeaseFence { get; set; }
    public long? LeaseExpiresAt { get; set; }
}
