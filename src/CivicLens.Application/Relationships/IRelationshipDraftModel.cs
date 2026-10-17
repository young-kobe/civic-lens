using CivicLens.Application.Analysis;

namespace CivicLens.Application.Relationships;

public interface IRelationshipDraftModel
{
    Task<AnalysisModelResponse> DraftAsync(AnalysisModelRequest request, CancellationToken cancellationToken);
}
