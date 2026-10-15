using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using CivicLens.Infrastructure.Analysis;

namespace CivicLens.Infrastructure.Statements;

public sealed class AnthropicStatementDraftModel(string apiKey, HttpClient? httpClient = null) : IStatementDraftModel
{
    private readonly AnthropicStructuredCall call = new(apiKey, httpClient);

    public Task<AnalysisModelResponse> DraftAsync(AnalysisModelRequest request, CancellationToken cancellationToken) =>
        call.SendAsync(request, cancellationToken);
}
