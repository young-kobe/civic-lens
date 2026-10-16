using CivicLens.Application.Analysis;
using CivicLens.Application.Briefs;
using CivicLens.Infrastructure.Analysis;

namespace CivicLens.Infrastructure.Briefs;

public sealed class AnthropicBriefDraftModel(string apiKey, HttpClient? httpClient = null) : IBriefDraftModel
{
    private readonly AnthropicStructuredCall call = new(apiKey, httpClient);

    public Task<AnalysisModelResponse> DraftAsync(AnalysisModelRequest request, CancellationToken cancellationToken) =>
        call.SendAsync(request, cancellationToken);
}
