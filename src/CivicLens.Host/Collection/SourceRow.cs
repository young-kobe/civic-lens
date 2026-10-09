using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Health;

namespace CivicLens.Host.Collection;

public sealed record SourceRow(string SourceId, string Label, string Location, string Officials, string Type,
    string Url, bool Enabled, SourceHealth Health)
{
    public bool CanCheck => Enabled && Health.State != SourceCheckState.Checking;

    public bool Matches(string? query) => string.IsNullOrWhiteSpace(query) ||
        new[] { Label, Officials, Url }.Any(value => value.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<SourceRow> From(CollectionConfiguration configuration, SourceHealthReport health)
    {
        var bySource = health.Sources.ToDictionary(item => item.SourceId, StringComparer.Ordinal);
        return configuration.Sources.Select(source => new SourceRow(source.Id, SourceLabels.Document(source.Url),
            SourceLabels.Location(source.Url), SourceLabels.Officials(configuration, source), SourceLabels.Type(source.Mode),
            source.Url, source.Enabled, bySource[source.Id])).ToArray();
    }
}
