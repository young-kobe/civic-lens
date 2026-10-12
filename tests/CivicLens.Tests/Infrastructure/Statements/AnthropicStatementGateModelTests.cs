using System.Net;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Statements;
using static CivicLens.Tests.Fixtures.StatementGateFixtures;

namespace CivicLens.Tests.Infrastructure.Statements;

public sealed class AnthropicStatementGateModelTests
{
    private const string Message = """
        {"id":"msg_fixture","type":"message","role":"assistant","model":"claude-haiku-5-5","content":[{"type":"text","text":"{}"}],
         "stop_reason":"end_turn","stop_sequence":null,
         "usage":{"input_tokens":10,"output_tokens":5,"cache_read_input_tokens":3,"cache_creation_input_tokens":7}}
        """;

    [Fact]
    public async Task TheGateMarksTheEndOfTheSystemPromptAsAnEphemeralCacheBreakpoint()
    {
        var handler = new CapturingHandler();
        using var client = new HttpClient(handler);
        var request = StatementGatePrompt.Build(Evidence("Rent cap."), Rubric(), StatementGateTask.MaximumPromptBytes)!;

        var response = await new AnthropicStatementGateModel("test-key", client).ClassifyAsync(request, CancellationToken.None);

        var system = JsonDocument.Parse(handler.Body).RootElement.GetProperty("system");
        var block = Assert.Single(system.EnumerateArray());
        Assert.Equal(request.SystemPrompt, block.GetProperty("text").GetString());
        Assert.Equal("ephemeral", block.GetProperty("cache_control").GetProperty("type").GetString());
        Assert.Equal((10, 5, 3, 7), (response.Usage.InputTokens, response.Usage.OutputTokens, response.Usage.CacheReadTokens,
            response.Usage.CacheWriteTokens));
    }

    [Fact]
    public async Task DraftingKeepsAPlainSystemPromptWithNoCacheBreakpoint()
    {
        var handler = new CapturingHandler();
        using var client = new HttpClient(handler);
        var request = new DocumentChangeDraftingRequest("claude-haiku-5-5", 100, "System.", "User.", """{"type":"object"}""",
            new string('a', 64), 1_000);

        await new AnthropicDocumentChangeDraftingModel("test-key", client).DraftAsync(request, CancellationToken.None);

        Assert.Equal(JsonValueKind.String, JsonDocument.Parse(handler.Body).RootElement.GetProperty("system").ValueKind);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Message, Encoding.UTF8, "application/json") };
        }
    }
}
