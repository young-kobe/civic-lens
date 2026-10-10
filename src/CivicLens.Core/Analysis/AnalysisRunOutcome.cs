namespace CivicLens.Core.Analysis;

public enum AnalysisRunOutcome
{
    Drafted,
    DraftDiscarded,
    CitationRejected,
    OutputRejected,
    Refused,
    OutputLimitReached,
    UnexpectedStop,
    RateLimited,
    ProviderUnavailable,
    AuthenticationFailed,
    ProviderRejected,
    ConnectionFailed,
    BillingFailed,
    ModelNotFound,
    ProcessingFailed,
    InvalidResponse,
    Interrupted
}
