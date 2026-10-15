using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public interface IStatementDraftModel
{
    Task<AnalysisModelResponse> DraftAsync(AnalysisModelRequest request, CancellationToken cancellationToken);
}
