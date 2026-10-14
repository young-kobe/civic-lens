using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Host.Analysis;

namespace CivicLens.Host.Actions;

internal sealed record ActionDraftConfiguration(ActionDraftSettings Settings, string ApiKey)
{
    private const string DailyTokensVariable = "CIVIC_LENS_ACTION_DRAFTING_DAILY_TOKENS";
    private const string RunTokensVariable = "CIVIC_LENS_ACTION_DRAFTING_RUN_TOKENS";
    private const string ApiKeyVariable = "ANTHROPIC_API_KEY";

    public static ActionDraftConfiguration? FromEnvironment()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DailyTokensVariable))) return null;
        var apiKey = Environment.GetEnvironmentVariable(ApiKeyVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException($"Action drafting needs both {DailyTokensVariable} and {ApiKeyVariable}.");
        var settings = new ActionDraftSettings(DocumentChangeAnalysisConfiguration.ReadTokens(DailyTokensVariable)!.Value,
            DocumentChangeAnalysisConfiguration.ReadTokens(RunTokensVariable),
            DocumentChangeAnalysisConfiguration.ReadConcurrency() ?? AnalysisSettings.DefaultConcurrency);
        return new(settings, apiKey);
    }
}
