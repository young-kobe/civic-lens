using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection;

namespace CivicLens.Application.Statements;

public sealed class StatementSpeakerRoster
{
    private ParsedRevision? lastRead;

    public ImmutableArray<AnalysisOfficial> OfficialsOn(CollectionConfigurationRevision? revision, string sourceId, DateOnly date)
    {
        if (revision is null) return [];
        var configuration = ConfigurationOf(revision);
        var source = configuration.Sources.SingleOrDefault(candidate => candidate.Id == sourceId);
        if (source is null) return [];
        var names = configuration.People.ToDictionary(person => person.Id, person => person.Name, StringComparer.Ordinal);
        return [.. configuration.CoveredPersonIdsOf(source, date).Select(id => new AnalysisOfficial(id, names[id]))];
    }

    public CollectionConfiguration ConfigurationOf(CollectionConfigurationRevision revision)
    {
        var memo = lastRead;
        if (memo is not null && memo.RevisionId == revision.Id) return memo.Configuration;
        var configuration = revision.ReadConfiguration();
        lastRead = new(revision.Id, configuration);
        return configuration;
    }

    private sealed record ParsedRevision(string RevisionId, CollectionConfiguration Configuration);
}
