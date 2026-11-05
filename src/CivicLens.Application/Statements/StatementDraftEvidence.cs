using CivicLens.Application.Collection;

namespace CivicLens.Application.Statements;

public sealed record StatementDraftEvidence(StatementArticle Article, StatementGateCarryOver CarryOver, StatementExistingDraft? ExistingDraft,
    CollectionConfigurationRevision? ConfigurationRevision)
{
    public int NextRevisionNumber => (ExistingDraft?.CurrentRevisionNumber ?? 0) + 1;
}
