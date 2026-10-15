using CivicLens.Application.Analysis;

namespace CivicLens.Host.Analysis;

internal sealed record DocumentChangeDraftConfiguration(DocumentChangeDraftSettings Settings, string ApiKey)
{
    public static DocumentChangeDraftConfiguration? FromEnvironment()
    {
        var environment = AnalysisEnvironment.Read("CIVIC_LENS_ANALYSIS_", "AI drafting");
        if (environment is null) return null;
        var settings = new DocumentChangeDraftSettings(environment.DailyTokens, environment.RunTokens,
            environment.Concurrency ?? DocumentChangeDraftSettings.DefaultConcurrency);
        return new(settings, environment.ApiKey);
    }
}
