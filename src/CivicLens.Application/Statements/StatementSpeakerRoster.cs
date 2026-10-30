using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection;

namespace CivicLens.Application.Statements;

public sealed record StatementSourceSpeakers(ImmutableArray<AnalysisOfficial> Officials, string? OfficeOf);

public sealed class StatementSpeakerRoster
{
    public ImmutableArray<AnalysisOfficial> OfficialsOn(CollectionConfigurationRevision? revision, string sourceId, DateOnly date) =>
        SpeakersOn(revision, sourceId, date).Officials;

    public StatementSourceSpeakers SpeakersOn(CollectionConfigurationRevision? revision, string sourceId, DateOnly date)
    {
        var source = revision?.Configuration.Sources.SingleOrDefault(candidate => candidate.Id == sourceId);
        if (revision is null || source is null) return new([], null);
        var configuration = revision.Configuration;
        var names = configuration.People.ToDictionary(person => person.Id, person => person.Name, StringComparer.Ordinal);
        return new([.. configuration.CoveredPersonIdsOf(source, date).Select(id => new AnalysisOfficial(id, names[id]))], source.OfficeOf);
    }
}
