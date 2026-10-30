using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Application.Configuration;
using CivicLens.Application.Issues;
using CivicLens.Collection.Contracts;

namespace CivicLens.Host;

internal static class ConfigurationFile
{
    private const int MaximumBytes = 1_000_000;

    public static CollectionConfiguration ReadCollection(string path)
    {
        var configuration = Read<CollectionConfiguration>(path, ConfigurationJson.Options);
        configuration.Validate();
        if (configuration.Sources.FirstOrDefault(source => source is { Enabled: true, ApiParser: ApiParser.SenateVoteList }) is { } retired)
            throw new RetiredSourceException(retired.Id);
        return configuration;
    }

    public static CollectionConfiguration? ReadCollectionFromEnvironment() =>
        AbsolutePathFromEnvironment("CIVIC_LENS_COLLECTION_CONFIG") is { } path ? ReadCollection(path) : null;

    public static IssueConfiguration? ReadIssuesFromEnvironment()
    {
        if (AbsolutePathFromEnvironment("CIVIC_LENS_ISSUES_CONFIG") is not { } path) return null;
        var configuration = Read<IssueConfiguration>(path, CollectionProtocol.JsonOptions);
        configuration.Validate();
        return configuration;
    }

    private static string? AbsolutePathFromEnvironment(string variable)
    {
        var path = Environment.GetEnvironmentVariable(variable);
        if (path is null) return null;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException($"{variable} must name an absolute path.");
        return path;
    }

    private static T Read<T>(string path, JsonSerializerOptions options) where T : class
    {
        using var input = File.OpenRead(path);
        var buffer = new byte[MaximumBytes + 1];
        var count = input.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        if (count > MaximumBytes) throw new ArgumentException("Configuration exceeds 1 MB.");
        return JsonSerializer.Deserialize<T>(buffer.AsSpan(0, count), options)
            ?? throw new ArgumentException("Configuration must not be null.");
    }
}
