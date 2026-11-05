using CivicLens.Application.Analysis;

namespace CivicLens.Application.Actions;

public sealed record ActionDraftSettings : AnalysisSettings
{
    public ActionDraftSettings(long dailyTokenLimit, int concurrency = DefaultConcurrency, bool draftsFromBillText = false)
        : base(dailyTokenLimit, concurrency, ActionDraftPrompt.MinimumReservation) => DraftsFromBillText = draftsFromBillText;

    /// <summary>True when a bill with no CRS summary gets an AI draft from its bill text. Off, the step writes the text by code.</summary>
    public bool DraftsFromBillText { get; }

    public int PromptByteCap => (int)Math.Min(ActionDraftTask.MaximumPromptBytes, CallTokenLimit - ActionDraftTask.MaximumOutputTokens);
}
