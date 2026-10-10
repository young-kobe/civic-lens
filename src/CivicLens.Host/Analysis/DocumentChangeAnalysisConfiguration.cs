using System.Globalization;
using CivicLens.Application.Analysis;

namespace CivicLens.Host.Analysis;

internal sealed record DocumentChangeAnalysisConfiguration(DocumentChangeAnalysisSettings Settings, string ApiKey)
{
    private const string DailyTokensVariable = "CIVIC_LENS_ANALYSIS_DAILY_TOKENS";
    private const string RunTokensVariable = "CIVIC_LENS_ANALYSIS_RUN_TOKENS";
    private const string ConcurrencyVariable = "CIVIC_LENS_ANALYSIS_CONCURRENCY";
    private const string ApiKeyVariable = "ANTHROPIC_API_KEY";
    private const long MaximumTokenLimit = 1_000_000_000_000;

    public static DocumentChangeAnalysisConfiguration? FromEnvironment()
    {
        var daily = Environment.GetEnvironmentVariable(DailyTokensVariable);
        var apiKey = Environment.GetEnvironmentVariable(ApiKeyVariable);
        if (string.IsNullOrWhiteSpace(daily) && string.IsNullOrWhiteSpace(apiKey)) return null;
        if (string.IsNullOrWhiteSpace(daily) || string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException($"AI drafting needs both {DailyTokensVariable} and {ApiKeyVariable}.");
        var settings = new DocumentChangeAnalysisSettings(ReadTokens(DailyTokensVariable)!.Value,
            ReadTokens(RunTokensVariable) ?? DocumentChangeAnalysisSettings.DefaultRunTokenLimit,
            ReadConcurrency() ?? DocumentChangeAnalysisSettings.DefaultConcurrency);
        return new(settings, apiKey);
    }

    private static long? ReadTokens(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed is < 1 or > MaximumTokenLimit)
            throw new ArgumentException($"{name} must be a whole number from 1 to {MaximumTokenLimit}.");
        return parsed;
    }

    private static int? ReadConcurrency()
    {
        var value = Environment.GetEnvironmentVariable(ConcurrencyVariable);
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            throw new ArgumentException($"{ConcurrencyVariable} must be a whole number.");
        return parsed;
    }
}
