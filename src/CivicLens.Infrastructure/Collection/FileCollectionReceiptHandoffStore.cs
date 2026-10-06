using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;

namespace CivicLens.Infrastructure.Collection;

public sealed class FileCollectionReceiptHandoffStore : ICollectionReceiptHandoffStore
{
    private const int MaximumEnvelopeBytes = 1_048_576;
    private readonly string root;
    private readonly string spool;

    public FileCollectionReceiptHandoffStore(string artifactRoot)
    {
        root = Path.GetFullPath(artifactRoot);
        Directory.CreateDirectory(root);
        EnsureDirectory(root);
        spool = Path.Combine(root, ".pending");
        Directory.CreateDirectory(spool);
        EnsureDirectory(spool);
    }

    public async ValueTask<IAsyncDisposable> AcquireRecoveryLeaseAsync(CancellationToken cancellationToken)
    {
        EnsureDirectories();
        var path = Path.Combine(spool, ".lock");
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Receipt handoff lock cannot be a symbolic link.");
            try
            {
                var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                try
                {
                    EnsureDirectories();
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Receipt handoff lock cannot be a symbolic link.");
                    return new Lease(stream);
                }
                catch
                {
                    await stream.DisposeAsync();
                    throw;
                }
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(50, cancellationToken);
            }
            catch (IOException exception)
            {
                throw new IOException("Receipt recovery is already active or the spool is unavailable.", exception);
            }
        }
    }

    public async Task SaveAsync(PendingCollectionHandoff handoff, CancellationToken cancellationToken)
    {
        EnsureDirectories();
        handoff.Validate();
        var target = GetPath(handoff.AttemptId);
        var contents = JsonSerializer.SerializeToUtf8Bytes(handoff, CollectionProtocol.JsonOptions);
        if (contents.Length > MaximumEnvelopeBytes)
            throw new InvalidDataException("Receipt handoff exceeds the size limit.");
        var temporary = Path.Combine(spool, ".tmp-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await file.WriteAsync(contents, cancellationToken);
                await file.FlushAsync(cancellationToken);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, target, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken)
    {
        EnsureDirectories();
        cancellationToken.ThrowIfCancellationRequested();
        var ids = Directory.EnumerateFiles(spool, "*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.Ordinal).ToArray();
        return Task.FromResult<IReadOnlyList<string>>(ids);
    }

    public async Task<PendingCollectionHandoff> LoadAsync(string handoffId, CancellationToken cancellationToken)
    {
        EnsureDirectories();
        var path = GetPath(handoffId);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Receipt handoffs cannot be symbolic links.");
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (file.Length is <= 0 or > MaximumEnvelopeBytes)
            throw new InvalidDataException("Receipt handoff size is invalid.");
        try
        {
            using var contents = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await file.ReadAsync(buffer, cancellationToken)) != 0)
            {
                if (contents.Length + read > MaximumEnvelopeBytes)
                    throw new InvalidDataException("Receipt handoff size is invalid.");
                await contents.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            var handoff = JsonSerializer.Deserialize<PendingCollectionHandoff>(contents.ToArray(),
                CollectionProtocol.JsonOptions) ?? throw new InvalidDataException("Receipt handoff is empty.");
            handoff.Validate();
            if (!string.Equals(handoff.AttemptId, handoffId, StringComparison.Ordinal))
                throw new InvalidDataException("Receipt handoff identity does not match its file name.");
            return handoff;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Receipt handoff JSON is invalid.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Receipt handoff data is invalid.", exception);
        }
    }

    public Task DeleteAsync(string handoffId, CancellationToken cancellationToken)
    {
        EnsureDirectories();
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(GetPath(handoffId));
        return Task.CompletedTask;
    }

    private string GetPath(string id) => PendingCollectionHandoff.IsValidAttemptId(id) ? Path.Combine(spool, id + ".json") :
        throw new InvalidDataException("Receipt handoff ID is invalid.");

    private void EnsureDirectories()
    {
        EnsureDirectory(root);
        EnsureDirectory(spool);
    }

    private static void EnsureDirectory(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Receipt handoff directories cannot be symbolic links.");
    }

    private sealed class Lease(FileStream stream) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }
}
