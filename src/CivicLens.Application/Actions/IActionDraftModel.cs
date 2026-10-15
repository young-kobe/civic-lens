using CivicLens.Application.Analysis;

namespace CivicLens.Application.Actions;

public interface IActionDraftModel
{
    Task<AnalysisModelResponse> DraftAsync(AnalysisModelRequest request, CancellationToken cancellationToken);
}
