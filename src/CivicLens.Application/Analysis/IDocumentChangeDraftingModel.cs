namespace CivicLens.Application.Analysis;

public interface IDocumentChangeDraftingModel
{
    Task<AnalysisModelResponse> DraftAsync(AnalysisModelRequest request, CancellationToken cancellationToken);
}
