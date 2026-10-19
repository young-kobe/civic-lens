using CivicLens.Application.Collection;
using CivicLens.Application.Review;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Analysis;

public sealed record DocumentChangeDraftEvidence(EligibleDocumentComparison Comparison, DocumentExtraction Before,
    DocumentExtraction After, string? ExistingDraftId, CollectionConfigurationRevision? ConfigurationRevision);
