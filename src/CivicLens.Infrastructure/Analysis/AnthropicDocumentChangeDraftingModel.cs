using CivicLens.Application.Analysis;

namespace CivicLens.Infrastructure.Analysis;

public sealed class AnthropicDocumentChangeDraftingModel(string apiKey, HttpClient? httpClient = null) : IDocumentChangeDraftingModel
{
    private readonly AnthropicStructuredCall call = new(apiKey, httpClient);

    public Task<AnalysisModelResponse> DraftAsync(AnalysisModelRequest request, CancellationToken cancellationToken) =>
        call.SendAsync(request, cancellationToken);
}
