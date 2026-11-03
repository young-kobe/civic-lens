using CivicLens.Application.Analysis;

namespace CivicLens.Application.Briefs;

public interface IBriefCheckModel
{
    Task<AnalysisModelResponse> CheckAsync(AnalysisModelRequest request, CancellationToken cancellationToken);
}
