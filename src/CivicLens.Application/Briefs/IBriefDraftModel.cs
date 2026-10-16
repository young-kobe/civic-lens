using CivicLens.Application.Analysis;

namespace CivicLens.Application.Briefs;

public interface IBriefDraftModel
{
    Task<AnalysisModelResponse> DraftAsync(AnalysisModelRequest request, CancellationToken cancellationToken);
}
