using CivicLens.Application.Analysis;

namespace CivicLens.Host.Analysis;

internal sealed record DocumentChangeAnalysisConfiguration(DocumentChangeAnalysisSettings Settings, string ApiKey)
{
    public static DocumentChangeAnalysisConfiguration? FromEnvironment()
    {
        var environment = AnalysisEnvironment.Read("CIVIC_LENS_ANALYSIS_", "AI drafting");
        if (environment is null) return null;
        var settings = new DocumentChangeAnalysisSettings(environment.DailyTokens, environment.RunTokens,
            environment.Concurrency ?? DocumentChangeAnalysisSettings.DefaultConcurrency);
        return new(settings, environment.ApiKey);
    }
}
