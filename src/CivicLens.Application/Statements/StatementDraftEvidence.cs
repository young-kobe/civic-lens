using CivicLens.Application.Collection;

namespace CivicLens.Application.Statements;

public sealed record StatementDraftEvidence(StatementArticle Article, StatementGateCarryOver CarryOver, string? ExistingDraftId,
    CollectionConfigurationRevision? ConfigurationRevision);
