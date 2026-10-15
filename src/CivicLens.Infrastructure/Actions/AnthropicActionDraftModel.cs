using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Infrastructure.Analysis;

namespace CivicLens.Infrastructure.Actions;

public sealed class AnthropicActionDraftModel(string apiKey, HttpClient? httpClient = null) : IActionDraftModel
{
    private readonly AnthropicStructuredCall call = new(apiKey, httpClient);

    public Task<AnalysisModelResponse> DraftAsync(AnalysisModelRequest request, CancellationToken cancellationToken) =>
        call.SendAsync(request, cancellationToken);
}
