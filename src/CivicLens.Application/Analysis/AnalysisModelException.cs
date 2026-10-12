using CivicLens.Core.Analysis;

namespace CivicLens.Application.Analysis;

public enum AnalysisModelFailure
{
    RateLimited,
    ProviderUnavailable,
    ConnectionFailed,
    AuthenticationFailed,
    BillingFailed,
    ModelNotFound,
    ProviderRejected,
    InvalidResponse
}

public sealed class AnalysisModelException(AnalysisModelFailure kind, Exception? inner = null,
    AnalysisTokenUsage? usage = null) : Exception($"The drafting model call failed: {kind}.", inner)
{
    public AnalysisModelFailure Kind { get; } = kind;
    public AnalysisTokenUsage? Usage { get; } = usage;
}
