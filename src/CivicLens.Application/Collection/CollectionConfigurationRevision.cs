using System.Text.Json.Serialization;
using CivicLens.Application.Configuration;

namespace CivicLens.Application.Collection;

public sealed record CollectionConfigurationRevision
{
    private static readonly ConfigurationSnapshotRules<CollectionConfiguration> Rules = new(static configuration => configuration.Validate());

    [JsonConstructor]
    public CollectionConfigurationRevision(string id, string json)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(json);

        Configuration = Rules.Verify(id, json);

        Id = id;
        Json = json;
    }

    public string Id { get; }

    public string Json { get; }

    [JsonIgnore]
    public CollectionConfiguration Configuration { get; }

    public CollectionConfiguration ReadConfiguration() => Rules.Parse(Json);

    public bool Equals(CollectionConfigurationRevision? other) => other is not null && Id == other.Id;

    public override int GetHashCode() => Id.GetHashCode(StringComparison.Ordinal);

    public static CollectionConfigurationRevision Create(CollectionConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var json = Rules.Snapshot(configuration);
        return new CollectionConfigurationRevision(Rules.ComputeId(json), json);
    }
}
