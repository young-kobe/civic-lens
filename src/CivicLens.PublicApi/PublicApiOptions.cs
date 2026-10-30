using System.Globalization;
using System.Net;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Reports;
using Npgsql;

namespace CivicLens.PublicApi;

public sealed record PublicApiOptions
{
    public const string ReleaseBaseUrlVariable = "CIVIC_LENS_RELEASE_BASE_URL";
    public const string DatabaseVariable = "CIVIC_LENS_SEARCH_DATABASE";
    public const string TrustedProxyRangesVariable = "CIVIC_LENS_TRUSTED_PROXY_RANGES";
    public const string RequestsPerMinuteVariable = "CIVIC_LENS_REQUESTS_PER_MINUTE";
    public const string ReportsPerHourVariable = "CIVIC_LENS_REPORTS_PER_HOUR";
    public const string ReportsPerDayVariable = "CIVIC_LENS_REPORTS_PER_DAY";
    public const string TurnstileSecretVariable = "CIVIC_LENS_TURNSTILE_SECRET";
    public const string TurnstileHostnameVariable = "CIVIC_LENS_TURNSTILE_HOSTNAME";
    public const string ReportPullTokenVariable = "CIVIC_LENS_REPORT_PULL_TOKEN";

    public required Uri ReleaseBaseUrl { get; init; }
    public required string Database { get; init; }
    public IReadOnlyList<IPNetwork> TrustedProxyRanges { get; init; } = [];
    public int RequestsPerMinute { get; init; } = 60;
    public int ReportsPerHour { get; init; } = 5;
    public int ReportsPerDay { get; init; } = 200;
    /// <summary>The bot check of report submission. Null means that the report routes answer 503.</summary>
    public TurnstileSettings? Turnstile { get; init; }
    /// <summary>The shared secret of the pull route. Null means that the route does not exist.</summary>
    public string? ReportPullToken { get; init; }
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan QueryTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>The connection for searches. It limits the time of every statement, so one slow query cannot hold the database.</summary>
    public string ReaderDatabase => new NpgsqlConnectionStringBuilder(Database)
    {
        Options = $"-c statement_timeout={(int)QueryTimeout.TotalMilliseconds}"
    }.ConnectionString;

    public static PublicApiOptions FromEnvironment() => new()
    {
        ReleaseBaseUrl = Directory(Required(ReleaseBaseUrlVariable)),
        Database = Required(DatabaseVariable),
        TrustedProxyRanges = [.. (Environment.GetEnvironmentVariable(TrustedProxyRangesVariable) ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(IPNetwork.Parse)],
        RequestsPerMinute = ParseLimit(RequestsPerMinuteVariable, 60),
        ReportsPerHour = ParseLimit(ReportsPerHourVariable, 5),
        ReportsPerDay = ParseLimit(ReportsPerDayVariable, 200),
        Turnstile = ReadTurnstile(),
        ReportPullToken = ParsePullToken(Environment.GetEnvironmentVariable(ReportPullTokenVariable))
    };

    public static int ParseLimit(string variable, string? text, int fallback) => string.IsNullOrEmpty(text)
        ? fallback
        : int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) && limit >= 1
            ? limit
            : throw new ArgumentException($"{variable} must be a whole number of 1 or more.");

    private static int ParseLimit(string variable, int fallback) => ParseLimit(variable, Environment.GetEnvironmentVariable(variable), fallback);

    private static TurnstileSettings? ReadTurnstile() => Environment.GetEnvironmentVariable(TurnstileSecretVariable) is { Length: > 0 } secret
        ? new(secret, Required(TurnstileHostnameVariable))
        : null;

    public static string? ParsePullToken(string? token) => string.IsNullOrEmpty(token)
        ? null
        : ReaderReportProtocol.IsPullToken(token)
            ? token
            : throw new ArgumentException($"{ReportPullTokenVariable} must hold at least {ReaderReportProtocol.MinimumPullTokenCharacters} characters.");

    /// <summary>Gives a base address that ends with a slash, so relative release paths resolve under it.</summary>
    public static Uri Directory(string url) => new(url.EndsWith('/') ? url : url + "/", UriKind.Absolute);

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new ArgumentException($"The search service requires {name}.");
}
