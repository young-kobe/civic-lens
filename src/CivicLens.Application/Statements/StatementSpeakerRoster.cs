using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection;

namespace CivicLens.Application.Statements;

public static class StatementSpeakerRoster
{
    public static ImmutableArray<AnalysisOfficial> OfficialsOn(CollectionConfigurationRevision? revision, string sourceId, DateOnly date)
    {
        if (revision is null) return [];
        var configuration = revision.ReadConfiguration();
        var source = configuration.Sources.SingleOrDefault(candidate => candidate.Id == sourceId);
        if (source is null) return [];
        var names = configuration.People.ToDictionary(person => person.Id, person => person.Name, StringComparer.Ordinal);
        return [.. configuration.CoveredPersonIdsOf(source, date).Select(id => new AnalysisOfficial(id, names[id]))];
    }
}
