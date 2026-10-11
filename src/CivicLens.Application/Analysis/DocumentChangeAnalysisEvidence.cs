using CivicLens.Application.Review;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Analysis;

public sealed record DocumentChangeAnalysisEvidence(EligibleDocumentComparison Comparison, DocumentExtraction Before,
    DocumentExtraction After, string? ExistingDraftId);
