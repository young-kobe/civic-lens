namespace CivicLens.Infrastructure.Collection.Jobs.Persistence;

internal sealed class ConfigurationRevisionRow
{
    public string RevisionId { get; set; } = "";
    public string Json { get; set; } = "";
    public long CreatedAt { get; set; }
}
