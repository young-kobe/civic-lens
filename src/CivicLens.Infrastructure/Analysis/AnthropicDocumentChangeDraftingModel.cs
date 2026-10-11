using System.Net;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using Failure = CivicLens.Application.Analysis.DocumentChangeDraftingFailure;

namespace CivicLens.Infrastructure.Analysis;

public sealed class AnthropicDocumentChangeDraftingModel : IDocumentChangeDraftingModel
{
    private const string ProductionApi = "https://api.anthropic.com";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);
    private readonly AnthropicClient client;

    public AnthropicDocumentChangeDraftingModel(string apiKey, HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        client = new AnthropicClient
        {
            ApiKey = apiKey,
            AuthToken = null,
            BaseUrl = ProductionApi,
            MaxRetries = 0,
            Timeout = RequestTimeout,
            HttpClient = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan }
        };
    }

    public async Task<DocumentChangeDraftingResponse> DraftAsync(DocumentChangeDraftingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var parameters = new MessageCreateParams
        {
            Model = request.Model,
            MaxTokens = request.MaximumOutputTokens,
            System = request.SystemPrompt,
            Messages = [new() { Role = Role.User, Content = request.UserPrompt }],
            OutputConfig = new OutputConfig
            {
                Effort = Effort.Medium,
                Format = new JsonOutputFormat { Schema = ToSchema(request.SchemaJson) }
            }
        };
        try
        {
            var message = await client.Messages.Create(parameters, cancellationToken);
            var usage = message.Usage;
            var tokens = new AnalysisTokenUsage(usage.InputTokens, usage.OutputTokens, usage.CacheReadInputTokens ?? 0,
                usage.CacheCreationInputTokens ?? 0);
            var stopReason = message.StopReason?.Raw() ?? "none";
            var text = stopReason == "end_turn"
                ? string.Concat(message.Content.Select(block => block.Value).OfType<TextBlock>().Select(block => block.Text))
                : null;
            return new(stopReason, string.IsNullOrEmpty(text) ? null : text, tokens, message.ID);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (AnthropicApiException exception) { throw new DocumentChangeDraftingException(ForStatus(exception.StatusCode), exception); }
        catch (AnthropicIOException exception) { throw new DocumentChangeDraftingException(Failure.ConnectionFailed, exception); }
        catch (AnthropicInvalidDataException exception) { throw new DocumentChangeDraftingException(Failure.InvalidResponse, exception); }
        catch (JsonException exception) { throw new DocumentChangeDraftingException(Failure.InvalidResponse, exception); }
        catch (AnthropicException exception) { throw new DocumentChangeDraftingException(Failure.ProviderUnavailable, exception); }
        catch (OperationCanceledException exception) { throw new DocumentChangeDraftingException(Failure.ConnectionFailed, exception); }
        catch (HttpRequestException exception) { throw new DocumentChangeDraftingException(Failure.ConnectionFailed, exception); }
    }

    private static Failure ForStatus(HttpStatusCode status) => (int)status switch
    {
        401 or 403 => Failure.AuthenticationFailed,
        402 => Failure.BillingFailed,
        404 => Failure.ModelNotFound,
        408 or 409 or >= 500 => Failure.ProviderUnavailable,
        429 => Failure.RateLimited,
        _ => Failure.ProviderRejected
    };

    private static Dictionary<string, JsonElement> ToSchema(string schemaJson)
    {
        using var document = JsonDocument.Parse(schemaJson);
        return document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
    }
}
