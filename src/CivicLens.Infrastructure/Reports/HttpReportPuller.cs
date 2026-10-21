using System.Net.Http.Headers;
using System.Net.Http.Json;
using CivicLens.Application.Reports;
using CivicLens.Publication.Contracts;

namespace CivicLens.Infrastructure.Reports;

/// <summary>Pulls reader reports from the search service with its bearer token. The token never appears in a message or a log.</summary>
public sealed class HttpReportPuller(HttpClient http, Uri serviceAddress, string token) : IReaderReportSource
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task<PulledReportBatch> PullAsync(string? cursor, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Timeout);
        var query = cursor is null ? "" : $"?{ReaderReportProtocol.AfterParameter}={Uri.EscapeDataString(cursor)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(serviceAddress, ReaderReportProtocol.PullPath + query));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, deadline.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PulledReportBatch>(PublicationProtocol.JsonOptions, deadline.Token)
            ?? throw new InvalidDataException("The search service sent an empty reply.");
    }
}
