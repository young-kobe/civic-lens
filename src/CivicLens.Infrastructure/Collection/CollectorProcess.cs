using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;

namespace CivicLens.Infrastructure.Collection;

public sealed class CollectorProcess(string collectorAssembly, string dotnetExecutable = "dotnet") : ICollectorProcess
{
    public async Task<CollectionResult> RunAsync(CollectionRequest request, CancellationToken cancellationToken)
    {
        request.Validate();
        var assembly = Path.GetFullPath(collectorAssembly);
        if (!File.Exists(assembly))
            throw new FileNotFoundException("Build the collector before collecting.", assembly);
        Directory.CreateDirectory(request.ArtifactDirectory);
        // Serialize invocations sharing this capture directory. There is no durable scheduler yet.
        await using var lease = new FileStream(Path.Combine(request.ArtifactDirectory, ".collector.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var manifest = Path.Combine(request.ArtifactDirectory, $".request-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(request, CollectionProtocol.JsonOptions), cancellationToken);
            var start = new ProcessStartInfo(dotnetExecutable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false, true),
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false
            };
            start.ArgumentList.Add(assembly);
            start.ArgumentList.Add("collect");
            start.ArgumentList.Add(manifest);
            using var process = Process.Start(start) ?? throw new IOException("Could not start collector.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds + 5));
            var stdout = ReadBoundedAsync(process.StandardOutput, 65_536, deadline.Token);
            var stderr = ReadBoundedAsync(process.StandardError, 65_536, deadline.Token);
            // A malformed child must not deadlock the parent by filling either redirected pipe.
            _ = stdout.ContinueWith(_ => deadline.Cancel(), CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            _ = stderr.ContinueWith(_ => deadline.Cancel(), CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            try
            {
                await Task.WhenAll(process.WaitForExitAsync(deadline.Token), stdout, stderr);
                var lines = (await stdout).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (lines.Length != 1)
                    throw new InvalidDataException("Collector must emit exactly one JSONL result.");
                var result = JsonSerializer.Deserialize<CollectionResult>(lines[0], CollectionProtocol.JsonOptions)
                    ?? throw new InvalidDataException("Collector emitted a null result.");
                result.ValidateAgainst(request);
                var expectedExit = result.Outcome is CollectionOutcome.Captured or CollectionOutcome.NotModified ? 0 : 1;
                if (process.ExitCode != expectedExit)
                    throw new InvalidDataException("Collector exit code disagrees with its result.");
                if (result.Outcome == CollectionOutcome.Captured)
                    await VerifyCaptureAsync(request, result, deadline.Token);
                return result;
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
        }
        finally
        {
            File.Delete(manifest);
        }
    }

    public static async Task VerifyCaptureAsync(CollectionRequest request, CollectionResult result, CancellationToken cancellationToken = default)
    {
        result.ValidateAgainst(request);
        if (result.Outcome != CollectionOutcome.Captured)
            throw new InvalidDataException("Capture verification requires a captured result.");
        var path = Path.Combine(request.ArtifactDirectory, result.Capture!.RelativePath);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Capture artifacts cannot be symbolic links.");
        await using var file = File.OpenRead(path);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[8192];
        long bytes = 0;
        int read;
        while ((read = await gzip.ReadAsync(buffer, cancellationToken)) != 0)
        {
            bytes += read;
            if (bytes > request.MaxBytes || bytes > result.Capture!.ByteLength)
                throw new InvalidDataException("Capture exceeds reported resource usage.");
            hash.AppendData(buffer, 0, read);
        }
        if (bytes != result.Capture!.ByteLength || Convert.ToHexStringLower(hash.GetHashAndReset()) != result.Capture!.Sha256)
            throw new InvalidDataException("Capture hash does not match its receipt.");
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int limit, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
        {
            if (text.Length + read > limit)
                throw new InvalidDataException("Collector output exceeds the protocol limit.");
            text.Append(buffer, 0, read);
        }
        return text.ToString();
    }
}
