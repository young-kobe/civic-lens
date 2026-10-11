using System.Globalization;
using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using CivicLens.Host.Components.Ui;
using Codes = CivicLens.Application.Analysis.DocumentChangeAnalysisErrorCodes;
using State = CivicLens.Application.Analysis.DocumentChangeAnalysisState;

namespace CivicLens.Host.Analysis;

internal static class DocumentChangeAnalysisDisplay
{
    public static (Tone Tone, string Label) For(DocumentChangeAnalysisRecord? record) => record?.Status switch
    {
        null => (Tone.Neutral, "Not queued"),
        State.Pending => (Tone.Neutral, "Queued"),
        State.Running => (Tone.Live, "Drafting"),
        State.RetryWaiting => (Tone.Warn, $"Retry at {Time(record.RetryAt)}"),
        State.WaitingForBudget => (Tone.Warn, $"Waiting for budget until {Time(record.RetryAt)}"),
        State.Blocked => (Tone.Warn, record.ErrorCode == Codes.InputLimitExceeded ? "Too large for AI" : "Evidence unavailable"),
        State.Failed => (Tone.Bad, Failure(record.ErrorCode)),
        State.Succeeded => record.ErrorCode is null ? (Tone.Ok, "Drafted") : (Tone.Neutral, "Person drafting"),
        _ => (Tone.Neutral, "Unknown")
    };

    public static (Tone Tone, string Text) Notice(DocumentChangeAnalysisStatus? status, DateTimeOffset now) => status switch
    {
        null => (Tone.Warn, "AI drafting status is unknown. The worker has not started since the upgrade."),
        _ when status.IsPaused(now) => (Tone.Bad, Paused(status.PausedReason!, status.PausedUntilUtc)),
        { Enabled: false } => (Tone.Warn, $"AI drafting is off. Start drafts by hand. The worker reported this on {Reported(status.RecordedAtUtc)}."),
        _ => (Tone.Neutral, $"AI drafting is on. Daily limit: {status.DailyTokenLimit:N0} tokens. The worker reported this on {Reported(status.RecordedAtUtc)}.")
    };

    private static string Paused(string reason, DateTimeOffset? until) => Codes.ToOutcome(reason) switch
    {
        AnalysisRunOutcome.AuthenticationFailed => "AI drafting is paused. The Claude API rejected the key. Fix the key and restart the worker.",
        AnalysisRunOutcome.BillingFailed => "AI drafting is paused. The Claude account has a billing problem. Fix it and restart the worker.",
        AnalysisRunOutcome.ModelNotFound => "AI drafting is paused. The model is not available to this account. Fix it and restart the worker.",
        _ => $"AI drafting is paused. The Claude API is failing. The worker tries again at {Time(until)}."
    };

    private static string Failure(string? errorCode) => errorCode == Codes.AttemptsExhausted ? "Retries used up" : Codes.ToOutcome(errorCode) switch
    {
        AnalysisRunOutcome.Refused => "AI declined",
        AnalysisRunOutcome.CitationRejected => "Citations rejected",
        AnalysisRunOutcome.OutputRejected => "Output rejected",
        AnalysisRunOutcome.OutputLimitReached => "Output too long",
        AnalysisRunOutcome.ProviderRejected => "Request rejected",
        AnalysisRunOutcome.InvalidResponse => "Unreadable response",
        _ => "Failed"
    };

    private static string Reported(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd 'at' HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static string Time(DateTimeOffset? value) =>
        value is { } time ? time.UtcDateTime.ToString("HH:mm 'UTC'", CultureInfo.InvariantCulture) : "soon";
}
