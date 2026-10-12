using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public interface IStatementGateModel
{
    Task<AnalysisModelResponse> ClassifyAsync(AnalysisModelRequest request, CancellationToken cancellationToken);
}
