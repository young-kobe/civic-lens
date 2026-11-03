using CivicLens.Application.Analysis;
using CivicLens.Application.Briefs;
using CivicLens.Host.Analysis;

namespace CivicLens.Host.Briefs;

internal sealed record BriefCheckConfiguration(BriefCheckSettings Settings, string ApiKey)
{
    public static BriefCheckConfiguration? FromEnvironment()
    {
        var environment = AnalysisEnvironment.Read("CIVIC_LENS_BRIEF_CHECKING_", "Brief checking");
        if (environment is null) return null;
        var settings = new BriefCheckSettings(environment.DailyTokens,
            environment.Concurrency ?? AnalysisSettings.DefaultConcurrency);
        return new(settings, environment.ApiKey);
    }
}
