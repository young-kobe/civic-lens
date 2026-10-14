using CivicLens.Application.Analysis;

namespace CivicLens.Application.Actions;

public interface IActionDraftingModel
{
    Task<AnalysisModelResponse> DraftAsync(AnalysisModelRequest request, CancellationToken cancellationToken);
}
