using CivicLens.Application.Analysis;

namespace CivicLens.Infrastructure.Analysis;

public sealed class AnthropicDocumentChangeDraftModel(string apiKey, HttpClient? httpClient = null) : IDocumentChangeDraftModel
{
    private readonly AnthropicStructuredCall call = new(apiKey, httpClient);

    public Task<AnalysisModelResponse> DraftAsync(AnalysisModelRequest request, CancellationToken cancellationToken) =>
        call.SendAsync(request, cancellationToken);
}
