using CivicLens.Core.Documents;

namespace CivicLens.Application.Review;

public sealed record EligibleDocumentComparison(string SourceId, string RequestedUrl, string BeforeFinalUrl,
    string AfterFinalUrl, DateTimeOffset BeforeObservedAtUtc, DateTimeOffset AfterObservedAtUtc,
    DocumentComparison Comparison);
