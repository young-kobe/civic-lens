using System.Net.Http.Json;
using System.Text.Json;

namespace CivicLens.PublicApi.Reports;

/// <summary>
/// Checks a token with the Cloudflare siteverify API. It sends the token and the secret and nothing else.
/// Any failure to get a clear answer is <see cref="TurnstileOutcome.Unavailable"/>, so the check fails closed.
/// </summary>
public sealed class TurnstileVerifier(HttpClient http, TurnstileSettings settings) : ITurnstileVerifier
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
            if (!response.IsSuccessStatusCode) return TurnstileOutcome.Unavailable;
            var reply = await response.Content.ReadFromJsonAsync<SiteVerifyReply>(cancellationToken);
            return reply is { Success: true } && string.Equals(reply.Hostname, settings.Hostname, StringComparison.OrdinalIgnoreCase)
                ? TurnstileOutcome.Passed
                : reply is null ? TurnstileOutcome.Unavailable : TurnstileOutcome.Rejected;
        }
        catch (Exception exception) when (exception is (HttpRequestException or JsonException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            return TurnstileOutcome.Unavailable;
        }
    }

    private sealed record SiteVerifyReply(bool Success, string? Hostname);
}
