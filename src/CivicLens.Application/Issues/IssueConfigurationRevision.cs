using System.Text.Json.Serialization;
using CivicLens.Application.Configuration;

namespace CivicLens.Application.Issues;

public sealed record IssueConfigurationRevision
{
    private static readonly ConfigurationSnapshotRules<IssueConfiguration> Rules = new(static configuration => configuration.Validate());

    [JsonConstructor]
    public IssueConfigurationRevision(string id, string json)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(json);

        Rules.Verify(id, json);

        Id = id;
        Json = json;
    }

    public string Id { get; }

    public string Json { get; }

    public IssueConfiguration ReadConfiguration() => Rules.Parse(Json);

    public static IssueConfigurationRevision Create(IssueConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var json = Rules.Snapshot(configuration);
        return new IssueConfigurationRevision(Rules.ComputeId(json), json);
    }
}
