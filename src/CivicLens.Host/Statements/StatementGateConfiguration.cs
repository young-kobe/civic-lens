using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using CivicLens.Host.Analysis;

namespace CivicLens.Host.Statements;

internal sealed record StatementGateConfiguration(StatementGateSettings Settings, string ApiKey)
{
    private const string DailyTokensVariable = "CIVIC_LENS_STATEMENT_GATE_DAILY_TOKENS";
    private const string RunTokensVariable = "CIVIC_LENS_STATEMENT_GATE_RUN_TOKENS";
    private const string ApiKeyVariable = "ANTHROPIC_API_KEY";

    public static StatementGateConfiguration? FromEnvironment()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DailyTokensVariable))) return null;
        var apiKey = Environment.GetEnvironmentVariable(ApiKeyVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException($"The statement gate needs both {DailyTokensVariable} and {ApiKeyVariable}.");
        var settings = new StatementGateSettings(DocumentChangeAnalysisConfiguration.ReadTokens(DailyTokensVariable)!.Value,
            DocumentChangeAnalysisConfiguration.ReadTokens(RunTokensVariable),
            DocumentChangeAnalysisConfiguration.ReadConcurrency() ?? DocumentChangeAnalysisSettings.DefaultConcurrency);
        return new(settings, apiKey);
    }
}
