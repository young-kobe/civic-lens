using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Host.Analysis;
using CivicLens.PublicWeb.Publication;

namespace CivicLens.Host.Actions;

internal sealed record ActionDraftConfiguration(ActionDraftSettings Settings, string ApiKey)
{
    private const string BillTextVariable = "CIVIC_LENS_ACTION_DRAFTING_BILL_TEXT";
    private const string KeyTermsVariable = "CIVIC_LENS_ACTION_DRAFTING_KEY_TERMS";

    public static ActionDraftConfiguration? FromEnvironment()
    {
        var environment = AnalysisEnvironment.Read("CIVIC_LENS_ACTION_DRAFTING_", "Action drafting");
        if (environment is null) return null;
        var billText = ReadBillText();
        var settings = new ActionDraftSettings(environment.DailyTokens,
            environment.Concurrency ?? AnalysisSettings.DefaultConcurrency, billText, ReadKeyTermGlossary(billText));
        return new(settings, environment.ApiKey);
    }

    /// <summary>The glossary term names that key terms may not repeat, or null while the key term setting is off. The glossary stays in PublicWeb,
    /// the one owner of the approved wording. Only this composition root reads it and hands the names to the draft step.</summary>
    public static IReadOnlyList<string>? ReadKeyTermGlossary(bool billText)
    {
        var setting = Environment.GetEnvironmentVariable(KeyTermsVariable);
        if (string.IsNullOrWhiteSpace(setting)) return null;
        if (setting != "1") throw new ArgumentException($"{KeyTermsVariable} must be 1 or unset.");
        if (!billText) throw new ArgumentException($"{KeyTermsVariable} needs {BillTextVariable} set to 1, because only a bill text draft has key terms.");
        return [.. Glossary.Groups.SelectMany(group => group.Terms).Select(term => term.Name)];
    }

    /// <summary>The key term setting alone, for a command that reads drafts but sends no model call. It reads the same variables as the worker,
    /// so the command and the worker agree on which drafts are stale.</summary>
    public static bool ReadDraftsKeyTerms() => ReadKeyTermGlossary(ReadBillText()) is not null;

    private static bool ReadBillText()
    {
        var setting = Environment.GetEnvironmentVariable(BillTextVariable);
        if (string.IsNullOrWhiteSpace(setting)) return false;
        if (setting != "1") throw new ArgumentException($"{BillTextVariable} must be 1 or unset.");
        return true;
    }
}
