using CivicLens.Core.Analysis;

namespace CivicLens.Application.Analysis;

public static class AnalysisErrorCodes
{
    public const string DailyTokenLimit = "dailyTokenLimit";
    public const string MonthlyDollarCap = "monthlyDollarCap";
    public const string MonthlyCapNotSet = "monthlyCapNotSet";
    public const string ModelPriceMissing = "modelPriceMissing";
    public const string AttemptsExhausted = "attemptsExhausted";
    public const string ProviderOutage = "providerOutage";
    public const string InvalidRunInput = "invalidRunInput";

    public static string ForOutcome(AnalysisRunOutcome outcome)
    {
        var name = outcome.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    public static AnalysisRunOutcome? ToOutcome(string? errorCode) =>
        Enum.TryParse<AnalysisRunOutcome>(errorCode, ignoreCase: true, out var outcome) && ForOutcome(outcome) == errorCode ? outcome : null;
}
