using System.Net;
using System.Text;

namespace CivicLens.Tests.PublicApi;

/// <summary>Answers the siteverify call from memory and keeps each request body, so a test can see what left the service.</summary>
internal sealed class FakeTurnstile : HttpMessageHandler
{
    public const string Hostname = "civic-lens.test";

    public bool Success { get; set; } = true;
    public string ReplyHostname { get; set; } = Hostname;
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public string ErrorCodes { get; set; } = "";
    public string? RawBody { get; set; }
    public Exception? Failure { get; set; }
    public List<string> Bodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
        if (Failure is { } failure) throw failure;
        return new HttpResponseMessage(Status)
        {
            Content = new StringContent(
                RawBody ?? $$"""{"success":{{(Success ? "true" : "false")}},"hostname":"{{ReplyHostname}}","error-codes":[{{ErrorCodes}}]}""",
                Encoding.UTF8, "application/json")
        };
    }
}
