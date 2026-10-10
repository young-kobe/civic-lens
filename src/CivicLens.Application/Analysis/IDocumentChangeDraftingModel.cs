namespace CivicLens.Application.Analysis;

public interface IDocumentChangeDraftingModel
{
    Task<DocumentChangeDraftingResponse> DraftAsync(DocumentChangeDraftingRequest request, CancellationToken cancellationToken);
}
