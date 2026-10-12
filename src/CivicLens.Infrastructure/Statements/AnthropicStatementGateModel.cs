using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using CivicLens.Infrastructure.Analysis;

namespace CivicLens.Infrastructure.Statements;

public sealed class AnthropicStatementGateModel(string apiKey, HttpClient? httpClient = null) : IStatementGateModel
{
    private readonly AnthropicStructuredCall call = new(apiKey, httpClient);

    public Task<DocumentChangeDraftingResponse> ClassifyAsync(DocumentChangeDraftingRequest request, CancellationToken cancellationToken) =>
        call.SendAsync(request, cancellationToken);
}
