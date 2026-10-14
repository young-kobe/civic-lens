using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Infrastructure.Analysis;

namespace CivicLens.Infrastructure.Actions;

public sealed class AnthropicActionDraftingModel(string apiKey, HttpClient? httpClient = null) : IActionDraftingModel
{
    private readonly AnthropicStructuredCall call = new(apiKey, httpClient);

    public Task<AnalysisModelResponse> DraftAsync(AnalysisModelRequest request, CancellationToken cancellationToken) =>
        call.SendAsync(request, cancellationToken);
}
