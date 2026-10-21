using System.Net.Http.Headers;
using System.Net.Http.Json;
using CivicLens.Application.Reports;
using CivicLens.Publication.Contracts;

namespace CivicLens.Infrastructure.Reports;

/// <summary>
/// Pulls reader reports from the search service with its bearer token. The token never appears in a message or a log.
/// The service address may hold a path prefix. The pull route is relative to it.
/// </summary>
public sealed class HttpReportPuller(HttpClient http, Uri serviceAddress, string token) : IReaderReportSource
{
    public const int MaximumReplyBytes = 4 * 1024 * 1024;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly Uri pullAddress = new(
        serviceAddress.AbsoluteUri.EndsWith('/') ? serviceAddress : new Uri(serviceAddress.AbsoluteUri + "/"),
        ReaderReportProtocol.PullPath.TrimStart('/'));

    public async Task<PulledReportBatch> PullAsync(string? cursor, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Timeout);
        var address = cursor is null ? pullAddress : new Uri(pullAddress, $"?{ReaderReportProtocol.AfterParameter}={Uri.EscapeDataString(cursor)}");
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, deadline.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PulledReportBatch>(PublicationProtocol.JsonOptions, deadline.Token)
            ?? throw new InvalidDataException("The search service sent an empty reply.");
    }
}
