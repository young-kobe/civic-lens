using CivicLens.Application.Analysis;

namespace CivicLens.Application.Relationships;

public interface IRelationshipDraftStore : IAnalysisQueueStore<RelationshipDraftRun, RelationshipDraftView, RelationshipDraftListItem>
{
    Task<RelationshipDraftEvidence?> ReadEvidenceAsync(string pairId, CancellationToken cancellationToken);

    Task<int> RefreshPairsAsync(CancellationToken cancellationToken);

    Task<int> CountStatementsWithoutCongressAsync(CancellationToken cancellationToken);
}
