using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using CivicLens.Host.Analysis;

namespace CivicLens.Host.Statements;

internal sealed record StatementDraftConfiguration(StatementDraftSettings Settings, string ApiKey)
{
    public static StatementDraftConfiguration? FromEnvironment()
    {
        var environment = AnalysisEnvironment.Read("CIVIC_LENS_STATEMENT_DRAFTING_", "Statement drafting");
        if (environment is null) return null;
        var settings = new StatementDraftSettings(environment.DailyTokens,
            environment.Concurrency ?? AnalysisSettings.DefaultConcurrency);
        return new(settings, environment.ApiKey);
    }
}
