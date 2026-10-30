using System.Collections.Immutable;
using CivicLens.Core.Documents;
using CivicLens.Core.ExecutiveActions;

namespace CivicLens.Application.ExecutiveActions;

public sealed record ExecutiveActionReviewRead(ExecutiveActionReview Review, ImmutableDictionary<string, DocumentExtraction> Extractions);
