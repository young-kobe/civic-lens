using CivicLens.Application.Analysis;
using CivicLens.Application.Votes;
using CivicLens.Infrastructure.Analysis;

namespace CivicLens.Infrastructure.Votes;

public sealed class AnthropicVoteDraftModel(string apiKey, HttpClient? httpClient = null) : IVoteDraftModel
{
    private readonly AnthropicStructuredCall call = new(apiKey, httpClient);

    public Task<AnalysisModelResponse> DraftAsync(AnalysisModelRequest request, CancellationToken cancellationToken) =>
        call.SendAsync(request, cancellationToken);
}
