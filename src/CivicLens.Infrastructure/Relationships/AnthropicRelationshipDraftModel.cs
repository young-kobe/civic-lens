using CivicLens.Application.Analysis;
using CivicLens.Application.Relationships;
using CivicLens.Infrastructure.Analysis;

namespace CivicLens.Infrastructure.Relationships;

public sealed class AnthropicRelationshipDraftModel(string apiKey, HttpClient? httpClient = null) : IRelationshipDraftModel
{
    private readonly AnthropicStructuredCall call = new(apiKey, httpClient);

    public Task<AnalysisModelResponse> DraftAsync(AnalysisModelRequest request, CancellationToken cancellationToken) =>
        call.SendAsync(request, cancellationToken);
}
