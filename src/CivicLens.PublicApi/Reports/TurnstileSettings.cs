namespace CivicLens.PublicApi.Reports;

/// <summary>The secret of the site and the hostname that Cloudflare must report for a passed check.</summary>
public sealed record TurnstileSettings(string Secret, string Hostname);
