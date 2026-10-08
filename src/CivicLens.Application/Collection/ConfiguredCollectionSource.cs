using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection;

/// <summary>A detached admission input whose omitted date is resolved after the saved key is read.</summary>
public sealed class ConfiguredCollectionSource
{
    public ConfiguredCollectionSource(CollectionConfiguration configuration, string sourceId, DateOnly? asOf = null)
    {
        Revision = CollectionConfigurationRevision.Create(configuration);
        SourceId = sourceId;
        AsOf = asOf;
        _ = ReadSource();
    }

    public CollectionConfigurationRevision Revision { get; }
    public string SourceId { get; }
    public DateOnly? AsOf { get; }

    public CollectionJobDefinition CreateJobDefinition(DateOnly currentDate, CollectionJobDefinition? existing = null) =>
        PrepareDefinition(ReadSource(), currentDate, existing, article: false);

    public DiscoveryAdmissionRequest CreateDiscoveryAdmission(string attemptId, string idempotencyKey,
        DateOnly currentDate, CollectionJobDefinition? existingTemplate = null, bool recheckKnownCandidates = false)
    {
        var source = ReadSource();
        if (source.Mode is not (CollectionMode.Feed or CollectionMode.Html))
            throw new ArgumentException("Admission requires a configured feed or HTML discovery source.");
        var template = PrepareDefinition(source, currentDate, existingTemplate, article: true);
        var request = new DiscoveryAdmissionRequest(attemptId, idempotencyKey, template,
            source.AdmissionPolicy ?? new DiscoveryAdmissionPolicy(), source.Mode, recheckKnownCandidates);
        request.Validate();
        return request;
    }

    private CollectionJobDefinition PrepareDefinition(WatchedSourceConfiguration source, DateOnly currentDate,
        CollectionJobDefinition? existing, bool article)
    {
        existing?.Validate();
        var definition = CollectionJobDefinition.FromSource(source);
        if (article)
            definition = definition with { Mode = CollectionMode.Page, ETag = null, LastModified = null };

        if (existing is not null)
        {
            // Compare the request to the saved decision without reevaluating current eligibility.
            // Legacy keys retain unknown provenance and compare only their original execution settings.
            if (existing.ConfigurationRevision is not null)
            {
                definition = definition with
                {
                    ConfigurationRevision = Revision,
                    CoverageAsOf = AsOf ?? existing.CoverageAsOf
                };
            }
            if (definition != existing)
                throw new ArgumentException("The idempotency key belongs to different admission input.");
            return existing;
        }

        definition = definition with { ConfigurationRevision = Revision, CoverageAsOf = AsOf ?? currentDate };
        definition.Validate();
        return definition;
    }

    private WatchedSourceConfiguration ReadSource() =>
        Revision.ReadConfiguration().Sources.SingleOrDefault(source => source.Id == SourceId)
        ?? throw new ArgumentException("Unknown source ID.", nameof(SourceId));
}
