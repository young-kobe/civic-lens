using System.Text.Json;
using CivicLens.Collection.Contracts;
using CivicLens.Collector;

if (args is [] or ["--help"] or ["help"])
{
    Console.Error.WriteLine("Usage: CivicLens.Collector collect <manifest.json>");
    return 0;
}
if (args.Length != 2 || args[0] != "collect")
{
    Console.Error.WriteLine("Usage: CivicLens.Collector collect <manifest.json>");
    return 2;
}

CollectionRequest request;
try
{
    await using var input = new FileStream(args[1], FileMode.Open, FileAccess.Read, FileShare.Read);
    using var manifest = new MemoryStream();
    var buffer = new byte[8192];
    while (true)
    {
        var remaining = 64 * 1024 + 1 - (int)manifest.Length;
        if (remaining <= 0) throw new ArgumentException("Manifest must be no larger than 64 KiB.");
        var read = await input.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)));
        if (read == 0) break;
        await manifest.WriteAsync(buffer.AsMemory(0, read));
    }
    if (manifest.Length > 64 * 1024) throw new ArgumentException("Manifest must be no larger than 64 KiB.");
    request = JsonSerializer.Deserialize<CollectionRequest>(manifest.ToArray(), CollectionProtocol.JsonOptions)
        ?? throw new JsonException("Manifest must contain one request object.");
    request.Validate();
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
{
    Console.Error.WriteLine($"Invalid manifest: {exception.Message}");
    return 2;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
using var collector = new HttpCollector();
var result = await collector.FetchAsync(request, cancellation.Token);
Console.WriteLine(JsonSerializer.Serialize(result, CollectionProtocol.JsonOptions));
if (result.FailureCode is not null) Console.Error.WriteLine($"Collection {result.Outcome}: {result.FailureCode}");
return result.Outcome is CollectionOutcome.Captured or CollectionOutcome.NotModified ? 0 : 1;
