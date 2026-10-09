namespace CivicLens.Infrastructure.Collection.Jobs.Persistence;

internal sealed class CollectionScheduleRow
{
    public string SourceId { get; set; } = "";
    public string ConfigurationRevisionId { get; set; } = "";
    public string ConfigurationJson { get; set; } = "";
    public int IntervalSeconds { get; set; }
    public long NextDueUtcTicks { get; set; }
    public bool Enabled { get; set; }
}
