namespace CivicLens.PublicApi.Reports;

/// <summary>The bot check that stands before the database write. It is the external boundary to Cloudflare.</summary>
public interface ITurnstileVerifier
{
    Task<TurnstileOutcome> VerifyAsync(string token, CancellationToken cancellationToken);
}

public enum TurnstileOutcome { Passed, Rejected, Unavailable }
