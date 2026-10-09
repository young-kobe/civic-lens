using CivicLens.Core.Documents;
using CivicLens.Core.Review;

namespace CivicLens.Application.Review;

public sealed record PublishableDocumentChange(DocumentChangeReview Review, EligibleDocumentComparison Comparison,
    DocumentExtraction Before, DocumentExtraction After);
