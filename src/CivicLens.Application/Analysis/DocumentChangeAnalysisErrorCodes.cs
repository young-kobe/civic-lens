using CivicLens.Core.Analysis;

namespace CivicLens.Application.Analysis;

public static class DocumentChangeAnalysisErrorCodes
{
    public const string HumanDraftExists = "humanDraftExists";
    public const string DraftDiscarded = "draftDiscarded";
    public const string ComparisonUnavailable = "comparisonUnavailable";
    public const string InputLimitExceeded = "inputLimitExceeded";
    public const string DailyTokenLimit = "dailyTokenLimit";
    public const string AttemptsExhausted = "attemptsExhausted";
    public const string ProviderOutage = "providerOutage";

    public static string ForOutcome(AnalysisRunOutcome outcome)
    {
        var name = outcome.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    public static AnalysisRunOutcome? ToOutcome(string? errorCode) =>
        Enum.TryParse<AnalysisRunOutcome>(errorCode, ignoreCase: true, out var outcome) && ForOutcome(outcome) == errorCode ? outcome : null;
}
