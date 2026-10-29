using System.Diagnostics;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Host;

internal static class HostProcess
{
    public static Task<(int ExitCode, string Output, string Error)> RunAsync(string? database, params string[] arguments) =>
        RunWithCollectorHostAsync(database, null, arguments);

    public static readonly string[] DraftingVariables =
    [
        "ANTHROPIC_API_KEY", "CIVIC_LENS_ANALYSIS_DAILY_TOKENS", "CIVIC_LENS_ANALYSIS_CONCURRENCY",
        "CIVIC_LENS_STATEMENT_GATE_DAILY_TOKENS",
        "CIVIC_LENS_STATEMENT_DRAFTING_DAILY_TOKENS",
        "CIVIC_LENS_ACTION_DRAFTING_DAILY_TOKENS",
        "CIVIC_LENS_VOTE_DRAFTING_DAILY_TOKENS",
        "CIVIC_LENS_RELATIONSHIP_DRAFTING_DAILY_TOKENS",
    ];

    private static readonly string[] ConcurrencyVariables =
        ["CIVIC_LENS_EVIDENCE_CONCURRENCY", "CIVIC_LENS_COLLECTION_CONCURRENCY"];

    public static Task<(int ExitCode, string Output, string Error)> RunWithCollectorHostAsync(
        string? database, string? collectorHost, params string[] arguments) =>
        RunWithEnvironmentAsync(database, collectorHost, new Dictionary<string, string>(), arguments);

    public static async Task<(int ExitCode, string Output, string Error)> RunWithEnvironmentAsync(
        string? database, string? collectorHost, IReadOnlyDictionary<string, string> environment, params string[] arguments)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var assembly = Path.Combine(RepositoryRoot.FullName, "src", "CivicLens.Host", "bin", configuration,
            "net10.0", "CivicLens.Host.dll");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(assembly);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment.Remove("CIVIC_LENS_DATABASE");
        foreach (var name in DraftingVariables) start.Environment.Remove(name);
        foreach (var variable in ConcurrencyVariables) start.Environment.Remove(variable);
        foreach (var (name, value) in environment) start.Environment[name] = value;
        if (database is not null) start.Environment["CIVIC_LENS_DATABASE"] = database;
        if (collectorHost is not null) start.Environment["DOTNET_HOST_PATH"] = collectorHost;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            return (process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }
}
