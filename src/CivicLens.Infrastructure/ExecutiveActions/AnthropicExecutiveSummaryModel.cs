using CivicLens.Application.Analysis;
using CivicLens.Application.ExecutiveActions;
using CivicLens.Infrastructure.Analysis;

namespace CivicLens.Infrastructure.ExecutiveActions;

public sealed class AnthropicExecutiveSummaryModel(string apiKey, HttpClient? httpClient = null) : IExecutiveSummaryModel
{
    private readonly AnthropicStructuredCall call = new(apiKey, httpClient);

    public Task<AnalysisModelResponse> DraftAsync(AnalysisModelRequest request, CancellationToken cancellationToken) =>
        call.SendAsync(request, cancellationToken);
}
