using System.Net;
using System.Net.Http.Json;
using CivicLens.Infrastructure.Reports;
using CivicLens.Publication.Contracts;
using static CivicLens.Tests.Publication.Contracts.PulledReportTests;

namespace CivicLens.Tests.Infrastructure.Reports;

public sealed class HttpReportPullerTests
{
    private const string Token = "0123456789abcdef0123456789abcdef";

    private static readonly Uri Service = new("http://search.example.test:5100");

    [Fact]
    public async Task ThePullSendsTheBearerTokenAndTheOpaqueCursorToThePullRouteAndReadsTheBatch()
    {
        var sent = new List<HttpRequestMessage>();
        var report = Report();
        var puller = new HttpReportPuller(new(new Reply(request => { sent.Add(request); return Json(Batch(report) with { NextCursor = "epoch.7" }); })),
            Service, Token);

        var batch = await puller.PullAsync("epoch 6/+", default);

        var request = Assert.Single(sent);
        Assert.Equal("http://search.example.test:5100/internal/v1/reports?after=epoch%206%2F%2B", request.RequestUri!.AbsoluteUri);
        Assert.Equal(("Bearer", Token), (request.Headers.Authorization!.Scheme, request.Headers.Authorization.Parameter));
        Assert.Equal(("epoch.7", report.ReportId), (batch.NextCursor, Assert.Single(batch.Reports).ReportId));
    }

    [Fact]
    public async Task TheFirstPullHasNoCursorParameterSoTheServiceStartsFromTheFirstReport()
    {
        Uri? asked = null;
        var puller = new HttpReportPuller(new(new Reply(request => { asked = request.RequestUri; return Json(Batch()); })), Service, Token);

        await puller.PullAsync(null, default);

        Assert.Equal("", asked!.Query);
    }

    [Fact]
    public async Task AFailedCallThrowsWithoutTheTokenSoTheStageCountsItAndNeverLogsTheSecret()
    {
        var puller = new HttpReportPuller(new(new Reply(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized))), Service, Token);

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => puller.PullAsync(null, default));

        Assert.DoesNotContain(Token, failure.ToString(), StringComparison.Ordinal);
    }

    private static HttpResponseMessage Json(PulledReportBatch batch) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(batch, options: PublicationProtocol.JsonOptions) };

    private sealed class Reply(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }
}
