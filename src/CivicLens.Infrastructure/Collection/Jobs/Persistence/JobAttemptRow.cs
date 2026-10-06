namespace CivicLens.Infrastructure.Collection.Jobs.Persistence;

internal sealed class JobAttemptRow
{
    public string AttemptId { get; set; } = "";
    public string JobId { get; set; } = "";
    public int Sequence { get; set; }
    public string RequestJson { get; set; } = "";
    public long StartedAt { get; set; }
    public string? ResolutionJson { get; set; }
    public long? CompletedAt { get; set; }
}
