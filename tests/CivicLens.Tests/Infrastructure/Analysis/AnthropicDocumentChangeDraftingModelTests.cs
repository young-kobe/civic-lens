using System.Net;
using System.Text;
using CivicLens.Application.Analysis;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Tests.Fixtures;
using Failure = CivicLens.Application.Analysis.DocumentChangeDraftingFailure;

namespace CivicLens.Tests.Infrastructure.Analysis;

public sealed class AnthropicDocumentChangeDraftingModelTests
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

    private static async Task<DocumentChangeDraftingException> CallAsync(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        using var client = new HttpClient(new FixtureHandler(respond));
        var model = new AnthropicDocumentChangeDraftingModel("test-key", client);
        var request = DocumentChangeDraftingPrompt.Build(
            FakeDocumentChangeDraftingModel.Evidence("Fee: 10.\n", "Fee: 12.\n"), new DocumentChangeAnalysisCatalog([], []))!;
        return await Assert.ThrowsAsync<DocumentChangeDraftingException>(() => model.DraftAsync(request, CancellationToken.None));
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
