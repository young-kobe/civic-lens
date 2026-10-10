using CivicLens.Core.Analysis;

namespace CivicLens.Application.Analysis;

public enum DocumentChangeDraftingFailure
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

public sealed class DocumentChangeDraftingException(DocumentChangeDraftingFailure kind, Exception? inner = null,
    AnalysisTokenUsage? usage = null) : Exception($"The drafting model call failed: {kind}.", inner)
{
    public DocumentChangeDraftingFailure Kind { get; } = kind;
    public AnalysisTokenUsage? Usage { get; } = usage;
}
