using System.Globalization;
using System.Net;
using Npgsql;

namespace CivicLens.PublicApi;

public sealed record PublicApiOptions
{
    public const string ReleaseBaseUrlVariable = "CIVIC_LENS_RELEASE_BASE_URL";
    public const string DatabaseVariable = "CIVIC_LENS_SEARCH_DATABASE";
    public const string TrustedProxyRangesVariable = "CIVIC_LENS_TRUSTED_PROXY_RANGES";
    public const string RequestsPerMinuteVariable = "CIVIC_LENS_REQUESTS_PER_MINUTE";

    public required Uri ReleaseBaseUrl { get; init; }
    public required string Database { get; init; }
    public IReadOnlyList<IPNetwork> TrustedProxyRanges { get; init; } = [];
    public int RequestsPerMinute { get; init; } = 60;
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMinutes(2);
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
        RequestsPerMinute = Environment.GetEnvironmentVariable(RequestsPerMinuteVariable) is { Length: > 0 } text
            ? int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture)
            : 60
    };

    /// <summary>Gives a base address that ends with a slash, so relative release paths resolve under it.</summary>
    public static Uri Directory(string url) => new(url.EndsWith('/') ? url : url + "/", UriKind.Absolute);

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new ArgumentException($"The search service requires {name}.");
}
