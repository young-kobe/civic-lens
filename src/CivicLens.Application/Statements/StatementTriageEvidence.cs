using CivicLens.Application.Collection;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Statements;

public sealed record StatementTriageEvidence(StatementReviewListItem Item, DocumentExtraction Extraction,
    CollectionConfigurationRevision? ConfigurationRevision);
