using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection;

namespace CivicLens.Application.Statements;

public sealed class StatementSpeakerRoster
{
    private readonly CollectionConfiguration configuration;
    private readonly Dictionary<string, WatchedSourceConfiguration> sources;
    private readonly Dictionary<string, string> names;

    public StatementSpeakerRoster(CollectionConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        this.configuration = configuration;
        sources = configuration.Sources.ToDictionary(source => source.Id, StringComparer.Ordinal);
        names = configuration.People.ToDictionary(person => person.Id, person => person.Name, StringComparer.Ordinal);
    }

    public ImmutableArray<AnalysisOfficial> OfficialsOn(string sourceId, DateOnly date) =>
        sources.TryGetValue(sourceId, out var source)
            ? [.. configuration.CoveredPersonIdsOf(source, date).Select(id => new AnalysisOfficial(id, names[id]))]
            : [];
}
