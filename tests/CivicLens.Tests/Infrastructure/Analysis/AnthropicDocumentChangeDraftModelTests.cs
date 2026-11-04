using System.Net;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Tests.Fixtures;
using Failure = CivicLens.Application.Analysis.AnalysisModelFailure;

namespace CivicLens.Tests.Infrastructure.Analysis;

public sealed class AnthropicDocumentChangeDraftModelTests
{
    private const string ErrorBody = """{"type":"error","error":{"type":"error","message":"Fixture error."}}""";

    [Theory]
    [InlineData(401, Failure.AuthenticationFailed)]
    [InlineData(402, Failure.BillingFailed)]
    [InlineData(403, Failure.AuthenticationFailed)]
    [InlineData(404, Failure.ModelNotFound)]
    [InlineData(408, Failure.ProviderUnavailable)]
    [InlineData(409, Failure.ProviderUnavailable)]
    [InlineData(413, Failure.ProviderRejected)]
    [InlineData(429, Failure.RateLimited)]
    [InlineData(500, Failure.ProviderUnavailable)]
    [InlineData(529, Failure.ProviderUnavailable)]
    public async Task EachStatusMapsToOneFailureKind(int status, Failure expected)
    {
        var failure = await CallAsync(_ => new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(ErrorBody, Encoding.UTF8, "application/json")
        });

        Assert.Equal(expected, failure.Kind);
    }

    [Fact]
    public async Task ATimeoutIsAConnectionFailure()
    {
        var failure = await CallAsync(_ => throw new TaskCanceledException("Timed out.", new TimeoutException()));

        Assert.Equal(Failure.ConnectionFailed, failure.Kind);
    }

    [Fact]
    public async Task AnUnreadableBilledResponseIsAnInvalidResponse()
    {
        var failure = await CallAsync(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":42}""", Encoding.UTF8, "application/json")
        });

        Assert.Equal(Failure.InvalidResponse, failure.Kind);
    }

    [Fact]
    public async Task TheEffortOfTheRequestReachesTheProviderAndAnUnknownEffortNeverSendsACall()
    {
        var bodies = new List<string>();
        using var client = new HttpClient(new FixtureHandler(request =>
        {
            bodies.Add(request.Content!.ReadAsStringAsync().Result);
            return Reply("end_turn", "{}");
        }));
        var model = new AnthropicDocumentChangeDraftModel("test-key", client);

        await model.DraftAsync(Request(), CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => model.DraftAsync(Request() with { Effort = "extreme" }, CancellationToken.None));

        Assert.Equal("medium", JsonDocument.Parse(Assert.Single(bodies)).RootElement.GetProperty("output_config").GetProperty("effort").GetString());
    }

    [Theory]
    [InlineData("end_turn")]
    [InlineData("refusal")]
    [InlineData("max_tokens")]
    [InlineData("pause_turn")]
    public async Task EveryStopThatHasTextKeepsItBecauseARefusalOrATruncatedAnswerIsEvidenceOfWhatTheModelSaid(string stopReason)
    {
        using var client = new HttpClient(new FixtureHandler(_ => Reply(stopReason, "Exact words.")));

        var response = await new AnthropicDocumentChangeDraftModel("test-key", client).DraftAsync(Request(), CancellationToken.None);

        Assert.Equal((stopReason, "Exact words."), (response.StopReason, response.OutputJson));
    }

    [Fact]
    public async Task AStopWithNoTextKeepsNoOutput()
    {
        using var client = new HttpClient(new FixtureHandler(_ => Reply("refusal", "")));

        var response = await new AnthropicDocumentChangeDraftModel("test-key", client).DraftAsync(Request(), CancellationToken.None);

        Assert.Null(response.OutputJson);
    }

    private static AnalysisModelRequest Request() => DocumentChangeDraftPrompt.Build(
        FakeDocumentChangeDraftModel.Evidence("Fee: 10.\n", "Fee: 12.\n"), new DocumentChangeDraftCatalog([], []))!;

    private static HttpResponseMessage Reply(string stopReason, string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            id = "msg_fixture",
            type = "message",
            role = "assistant",
            model = "claude-haiku-5-5",
            content = new[] { new { type = "text", text } },
            stop_reason = stopReason,
            stop_sequence = (string?)null,
            usage = new { input_tokens = 10, output_tokens = 5, cache_read_input_tokens = 0, cache_creation_input_tokens = 0 }
        }), Encoding.UTF8, "application/json")
    };

    private static async Task<AnalysisModelException> CallAsync(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        using var client = new HttpClient(new FixtureHandler(respond));
        var model = new AnthropicDocumentChangeDraftModel("test-key", client);
        return await Assert.ThrowsAsync<AnalysisModelException>(() => model.DraftAsync(Request(), CancellationToken.None));
    }

    private sealed class FixtureHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("api.anthropic.com", request.RequestUri!.Host);
            Assert.False(request.Headers.Contains("Authorization"));
            return Task.FromResult(respond(request));
        }
    }
}
