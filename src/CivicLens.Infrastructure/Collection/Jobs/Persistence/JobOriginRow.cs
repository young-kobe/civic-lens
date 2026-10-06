namespace CivicLens.Infrastructure.Collection.Jobs.Persistence;

internal sealed class JobOriginRow
{
    public string Origin { get; set; } = "";
    public long NotBefore { get; set; }
    public string? UnresolvedAttemptId { get; set; }
}
