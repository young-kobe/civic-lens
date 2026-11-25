using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CivicLens.Publication.Contracts;
using Microsoft.Extensions.Logging;

namespace CivicLens.PublicApi.Reports;

public enum TurnstileOutcome { Passed, Rejected, Unavailable }

/// <summary>
/// Checks a token with the Cloudflare siteverify API. It sends the token and the secret and nothing else.
/// Any failure to get a clear answer, including the reply code "internal-error", is <see cref="TurnstileOutcome.Unavailable"/>, so the check fails closed.
/// </summary>
public sealed class TurnstileVerifier(HttpClient http, TurnstileSettings settings, ILogger<TurnstileVerifier> logger)
{
    public static readonly Uri SiteVerifyUrl = new("https://challenges.cloudflare.com/turnstile/v0/siteverify");
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public async Task<TurnstileOutcome> VerifyAsync(string token, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, SiteVerifyUrl)
            {
                Content = new FormUrlEncodedContent([new("secret", settings.Secret), new("response", token)])
            };
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Turnstile Siteverify returned HTTP {StatusCode}.", (int)response.StatusCode);
                return TurnstileOutcome.Unavailable;
            }
            var reply = await response.Content.ReadFromJsonAsync<SiteVerifyReply>(cancellationToken);
            if (reply is null || reply.HasInternalError)
            {
                logger.LogWarning("Turnstile Siteverify returned {Reason}.", reply is null ? "a null reply" : "internal-error");
                return TurnstileOutcome.Unavailable;
            }
            return reply.Success && reply.Action == ReaderReportProtocol.TurnstileAction &&
                string.Equals(reply.Hostname, settings.Hostname, StringComparison.OrdinalIgnoreCase)
                ? TurnstileOutcome.Passed
                : TurnstileOutcome.Rejected;
        }
        catch (Exception exception) when (exception is (HttpRequestException or JsonException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Turnstile Siteverify failed with {FailureType}.", exception.GetType().Name);
            return TurnstileOutcome.Unavailable;
        }
    }

    private sealed record SiteVerifyReply(bool Success, string? Hostname, string? Action, [property: JsonPropertyName("error-codes")] string[]? ErrorCodes)
    {
        public bool HasInternalError => ErrorCodes?.Contains("internal-error") == true;
    }
}
