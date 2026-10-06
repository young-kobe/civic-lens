using System.Text.Json;
using CivicLens.Application;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure;

if (args is [] or ["--help"] or ["help"])
{
    Console.WriteLine("Civic Lens: status | validate <config.json> | collect <config.json> <source-id> <collector.dll> <artifact-directory>");
    return 0;
}

if (args is ["status"])
{
    Console.WriteLine(FoundationStatus.Description);
    return 0;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    if (args is ["validate", _] or ["collect", _, _, _, _])
    {
        var configPath = args[1];
        await using var input = File.OpenRead(configPath);
        var buffer = new byte[1_000_001];
        var count = await input.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellation.Token);
        if (count > 1_000_000)
            throw new ArgumentException("Configuration exceeds 1 MB.");
        var configuration = JsonSerializer.Deserialize<CollectionConfiguration>(
            buffer.AsSpan(0, count), CollectionProtocol.JsonOptions)
            ?? throw new ArgumentException("Configuration must not be null.");
        configuration.Validate();
        if (args[0] == "validate")
        {
            Console.WriteLine($"Valid configuration: {configuration.People.Length} people, {configuration.Sources.Length} watched sources.");
            return 0;
        }
        var runner = new CollectorProcess(args[3], Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet");
        var result = await new CollectWatchedPage(runner).ExecuteAsync(configuration, args[2], args[4], cancellation.Token);
        Console.WriteLine(JsonSerializer.Serialize(result, CollectionProtocol.JsonOptions));
        return result.Outcome is CollectionOutcome.Captured or CollectionOutcome.NotModified ? 0 : 1;
    }
    Console.Error.WriteLine("Unknown command. Use --help.");
    return 2;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Collection cancelled or timed out. No receipt was accepted.");
    return 1;
}
catch (Exception exception) when (exception is ArgumentException or UnauthorizedAccessException or IOException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}
