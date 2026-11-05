using CivicLens.Core.Analysis;

namespace CivicLens.Application.Analysis;

public static class AnalysisFailureRules
{
    /// <summary>A connection failure costs the whole reservation, because the provider may have run the call. A bad response costs its usage when it has one.</summary>
    public static long ChargeFor(AnalysisModelException exception, long reservedTokens) => exception.Kind switch
    {
        AnalysisModelFailure.ConnectionFailed => reservedTokens,
        AnalysisModelFailure.InvalidResponse => exception.Usage?.Total ?? reservedTokens,
        AnalysisModelFailure.RateLimited or AnalysisModelFailure.ProviderUnavailable or AnalysisModelFailure.AuthenticationFailed or
            AnalysisModelFailure.BillingFailed or AnalysisModelFailure.ModelNotFound or AnalysisModelFailure.ProviderRejected => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(exception), exception.Kind, "Model failure has no token charge.")
    };

    /// <summary>Every later call fails the same way until a person acts, so the worker pauses and the measure stops.</summary>
    public static bool NeedsPerson(AnalysisModelFailure kind) =>
        kind is AnalysisModelFailure.AuthenticationFailed or AnalysisModelFailure.BillingFailed or AnalysisModelFailure.ModelNotFound;
}
