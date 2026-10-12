using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public interface IStatementGateModel
{
    Task<DocumentChangeDraftingResponse> ClassifyAsync(DocumentChangeDraftingRequest request, CancellationToken cancellationToken);
}
