using CivicLens.Application.Analysis;

namespace CivicLens.Application.Actions;

public sealed record ActionDraftSettings : AnalysisSettings
{
    public ActionDraftSettings(long dailyTokenLimit, int concurrency = DefaultConcurrency, bool draftsFromBillText = false,
        IEnumerable<string>? keyTermGlossary = null)
        : base(dailyTokenLimit, concurrency, ActionDraftPrompt.MinimumReservation)
    {
        DraftsFromBillText = draftsFromBillText;
        KeyTermGlossary = keyTermGlossary?.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>True when a bill with no CRS summary gets an AI draft from its bill text. Off, the step writes the text by code.</summary>
    public bool DraftsFromBillText { get; }

    /// <summary>The glossary term names that key terms may not repeat, compared with case ignored. It is null while key terms are off, and a text draft then asks for
    /// no key terms. Key terms need <see cref="DraftsFromBillText"/>, because only a text draft carries them.</summary>
    public IReadOnlySet<string>? KeyTermGlossary { get; }

    public bool DraftsKeyTerms => KeyTermGlossary is not null;

    public int PromptByteCap => (int)Math.Min(ActionDraftTask.MaximumPromptBytes, CallTokenLimit - ActionDraftTask.MaximumOutputTokens);

    /// <summary>The byte cap of the text prompt. Its reservation holds the larger output limit of the text call, so it never passes the call limit.</summary>
    public int TextPromptByteCap => (int)Math.Min(ActionDraftTask.MaximumPromptBytes, CallTokenLimit - ActionDraftTask.MaximumTextOutputTokens);
}
