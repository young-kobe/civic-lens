using CivicLens.Application.Analysis;

namespace CivicLens.Application.Votes;

public interface IVoteDraftModel
{
    Task<AnalysisModelResponse> DraftAsync(AnalysisModelRequest request, CancellationToken cancellationToken);
}
