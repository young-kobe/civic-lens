using CivicLens.Application.Analysis;
using CivicLens.Application.Briefs;
using CivicLens.Host.Analysis;

namespace CivicLens.Host.Briefs;

internal sealed record BriefDraftConfiguration(BriefDraftSettings Settings, string ApiKey)
{
    public static BriefDraftConfiguration? FromEnvironment()
    {
        var environment = AnalysisEnvironment.Read("CIVIC_LENS_BRIEF_DRAFTING_", "Brief drafting");
        if (environment is null) return null;
        var settings = new BriefDraftSettings(environment.DailyTokens, environment.RunTokens,
            environment.Concurrency ?? AnalysisSettings.DefaultConcurrency);
        return new(settings, environment.ApiKey);
    }
}
