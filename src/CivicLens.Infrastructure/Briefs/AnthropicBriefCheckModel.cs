using CivicLens.Application.Analysis;
using CivicLens.Application.Briefs;
using CivicLens.Infrastructure.Analysis;

namespace CivicLens.Infrastructure.Briefs;

public sealed class AnthropicBriefCheckModel(string apiKey, HttpClient? httpClient = null) : IBriefCheckModel
{
    private readonly AnthropicStructuredCall call = new(apiKey, httpClient);

    public Task<AnalysisModelResponse> CheckAsync(AnalysisModelRequest request, CancellationToken cancellationToken) =>
        call.SendAsync(request, cancellationToken);
}
