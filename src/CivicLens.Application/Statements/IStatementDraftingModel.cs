using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public interface IStatementDraftingModel
{
    Task<AnalysisModelResponse> DraftAsync(AnalysisModelRequest request, CancellationToken cancellationToken);
}
