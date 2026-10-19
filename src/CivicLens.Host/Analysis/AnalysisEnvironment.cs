using System.Globalization;

namespace CivicLens.Host.Analysis;

internal sealed record AnalysisEnvironment(long DailyTokens, int? Concurrency, string ApiKey)
{
    private const string ConcurrencyVariable = "CIVIC_LENS_ANALYSIS_CONCURRENCY";
    private const string ApiKeyVariable = "ANTHROPIC_API_KEY";
    private const long MaximumTokenLimit = 1_000_000_000_000;

    public static AnalysisEnvironment? Read(string variablePrefix, string subject)
    {
        var dailyVariable = variablePrefix + "DAILY_TOKENS";
        var daily = Environment.GetEnvironmentVariable(dailyVariable);
        var apiKey = Environment.GetEnvironmentVariable(ApiKeyVariable);
        if (string.IsNullOrWhiteSpace(daily)) return null;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException($"{subject} needs both {dailyVariable} and {ApiKeyVariable}.");
        return new(ReadDailyTokens(dailyVariable), ReadConcurrency(), apiKey);
    }

    private static long ReadDailyTokens(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
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
