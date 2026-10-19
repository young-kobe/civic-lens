using CivicLens.Application.Analysis;
using CivicLens.Application.Relationships;
using CivicLens.Host.Analysis;

namespace CivicLens.Host.Relationships;

internal sealed record RelationshipDraftConfiguration(RelationshipDraftSettings Settings, string ApiKey)
{
    public static RelationshipDraftConfiguration? FromEnvironment()
    {
        var environment = AnalysisEnvironment.Read("CIVIC_LENS_RELATIONSHIP_DRAFTING_", "Relationship drafting");
        if (environment is null) return null;
        var settings = new RelationshipDraftSettings(environment.DailyTokens,
            environment.Concurrency ?? AnalysisSettings.DefaultConcurrency);
        return new(settings, environment.ApiKey);
    }
}
