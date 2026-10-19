using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Host.Analysis;

namespace CivicLens.Host.Actions;

internal sealed record ActionDraftConfiguration(ActionDraftSettings Settings, string ApiKey)
{
    public static ActionDraftConfiguration? FromEnvironment()
    {
        var environment = AnalysisEnvironment.Read("CIVIC_LENS_ACTION_DRAFTING_", "Action drafting");
        if (environment is null) return null;
        var settings = new ActionDraftSettings(environment.DailyTokens,
            environment.Concurrency ?? AnalysisSettings.DefaultConcurrency);
        return new(settings, environment.ApiKey);
    }
}
