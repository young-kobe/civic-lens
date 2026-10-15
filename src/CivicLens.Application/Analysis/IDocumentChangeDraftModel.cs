namespace CivicLens.Application.Analysis;

public interface IDocumentChangeDraftModel
{
    Task<AnalysisModelResponse> DraftAsync(AnalysisModelRequest request, CancellationToken cancellationToken);
}
