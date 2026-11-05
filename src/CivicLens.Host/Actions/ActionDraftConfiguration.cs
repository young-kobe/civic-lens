using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Host.Analysis;

namespace CivicLens.Host.Actions;

internal sealed record ActionDraftConfiguration(ActionDraftSettings Settings, string ApiKey)
{
    private const string BillTextVariable = "CIVIC_LENS_ACTION_DRAFTING_BILL_TEXT";

    public static ActionDraftConfiguration? FromEnvironment()
    {
        var environment = AnalysisEnvironment.Read("CIVIC_LENS_ACTION_DRAFTING_", "Action drafting");
        if (environment is null) return null;
        var settings = new ActionDraftSettings(environment.DailyTokens,
            environment.Concurrency ?? AnalysisSettings.DefaultConcurrency, ReadBillText());
        return new(settings, environment.ApiKey);
    }

    private static bool ReadBillText()
    {
        var setting = Environment.GetEnvironmentVariable(BillTextVariable);
        if (string.IsNullOrWhiteSpace(setting)) return false;
        if (setting != "1") throw new ArgumentException($"{BillTextVariable} must be 1 or unset.");
        return true;
    }
}
