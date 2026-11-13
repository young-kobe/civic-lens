using CivicLens.Application.Analysis;

namespace CivicLens.Application.ExecutiveActions;

public interface IExecutiveSummaryModel
{
    Task<AnalysisModelResponse> DraftAsync(AnalysisModelRequest request, CancellationToken cancellationToken);
}
