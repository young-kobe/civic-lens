using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using CivicLens.Host.Analysis;

namespace CivicLens.Host.Statements;

internal sealed record StatementDraftConfiguration(StatementDraftSettings Settings, string ApiKey)
{
    private const string DailyTokensVariable = "CIVIC_LENS_STATEMENT_DRAFTING_DAILY_TOKENS";
    private const string RunTokensVariable = "CIVIC_LENS_STATEMENT_DRAFTING_RUN_TOKENS";
    private const string ApiKeyVariable = "ANTHROPIC_API_KEY";

    public static StatementDraftConfiguration? FromEnvironment()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DailyTokensVariable))) return null;
        var apiKey = Environment.GetEnvironmentVariable(ApiKeyVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException($"Statement drafting needs both {DailyTokensVariable} and {ApiKeyVariable}.");
        var settings = new StatementDraftSettings(DocumentChangeAnalysisConfiguration.ReadTokens(DailyTokensVariable)!.Value,
            DocumentChangeAnalysisConfiguration.ReadTokens(RunTokensVariable),
            DocumentChangeAnalysisConfiguration.ReadConcurrency() ?? AnalysisSettings.DefaultConcurrency);
        return new(settings, apiKey);
    }
}
