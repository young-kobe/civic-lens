using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using CivicLens.Infrastructure.Analysis;

namespace CivicLens.Infrastructure.Statements;

public sealed class AnthropicStatementDraftingModel(string apiKey, HttpClient? httpClient = null) : IStatementDraftingModel
{
    private readonly AnthropicStructuredCall call = new(apiKey, httpClient);

    public Task<AnalysisModelResponse> DraftAsync(AnalysisModelRequest request, CancellationToken cancellationToken) =>
        call.SendAsync(request, cancellationToken);
}
