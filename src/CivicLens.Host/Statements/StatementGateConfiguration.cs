using CivicLens.Application.Statements;
using CivicLens.Application.Analysis;
using CivicLens.Host.Analysis;

namespace CivicLens.Host.Statements;

internal sealed record StatementGateConfiguration(StatementGateSettings Settings, string ApiKey)
{
    public static StatementGateConfiguration? FromEnvironment()
    {
        var environment = AnalysisEnvironment.Read("CIVIC_LENS_STATEMENT_GATE_", "The statement gate");
        if (environment is null) return null;
        var settings = new StatementGateSettings(environment.DailyTokens, environment.RunTokens,
            environment.Concurrency ?? AnalysisSettings.DefaultConcurrency);
        return new(settings, environment.ApiKey);
    }
}
