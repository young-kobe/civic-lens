using System.ComponentModel;
using System.Runtime.InteropServices;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Infrastructure.Publication;

public sealed class FileReleaseDirectory : IReleaseDirectory
{
    private readonly string rootPath;

    public FileReleaseDirectory(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Path.IsPathRooted(rootPath))
            throw new ArgumentException("The release root must be an absolute path.", nameof(rootPath));
        this.rootPath = Path.GetFullPath(rootPath);
    }

    private string CurrentPath => Path.Combine(rootPath, ReleasePaths.CurrentLink);

    public Task<IReleaseStaging> BeginAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReleasePaths.CreateReadableDirectory(rootPath);
        var staging = Path.Combine(rootPath, ReleasePaths.StagingFolder);
        ReleasePaths.CreateReadableDirectory(staging);
        var path = Path.Combine(staging, Guid.NewGuid().ToString("N"));
        ReleasePaths.CreateReadableDirectory(path);
        return Task.FromResult<IReleaseStaging>(new FileReleaseStaging(rootPath, path));
    }

    public async Task<byte[]> ReadFileAsync(string directoryName, string relativePath, int maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        var path = ReleasePaths.ResolveWithoutLinks(rootPath, directoryName, relativePath);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (stream.Length > maximumBytes) throw new InvalidDataException("Release file exceeds the allowed size.");
        var buffer = new byte[stream.Length];
        await stream.ReadExactlyAsync(buffer, cancellationToken);
        return buffer;
    }

    public Task<IReadOnlyList<string>> ListFilesAsync(string directoryName, string folder, int maximumFiles,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path;
        try
        {
            path = ReleasePaths.ResolveWithoutLinks(rootPath, directoryName, folder);
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        IReadOnlyList<string> files = [.. Directory.EnumerateFiles(path).Take(maximumFiles + 1)
            .Select(file => $"{folder}/{Path.GetFileName(file)}").Order(StringComparer.Ordinal)];
        if (files.Count > maximumFiles) throw new InvalidDataException("Release folder holds more files than allowed.");
        return Task.FromResult(files);
    }

    public Task DeleteAsync(string directoryName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReleasePaths.RequireDirectoryName(directoryName);
        var tombstone = MoveToTombstone(ReleasePath(directoryName));
        if (tombstone is not null) Directory.Delete(tombstone, recursive: true);
        return Task.CompletedTask;
    }

    public Task RetireOlderThanAsync(int releaseNumber, IReadOnlyCollection<string> keepDirectoryNames,
        CancellationToken cancellationToken)
    {
        var releases = Path.Combine(rootPath, ReleasePaths.ReleasesFolder);
        if (!Directory.Exists(releases)) return Task.CompletedTask;
        foreach (var path in Directory.EnumerateDirectories(releases))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            if (ReleasePaths.TryReleaseNumber(name, out var number) && number < releaseNumber && !keepDirectoryNames.Contains(name))
                _ = MoveToTombstone(path);
        }

        return Task.CompletedTask;
    }

    public Task DeleteRetiredAsync(CancellationToken cancellationToken)
    {
        var tombstones = Path.Combine(rootPath, ReleasePaths.TombstoneFolder);
        if (!Directory.Exists(tombstones)) return Task.CompletedTask;
        foreach (var tombstone in Directory.EnumerateDirectories(tombstones))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Directory.Delete(tombstone, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }

        return Task.CompletedTask;
    }

    // One rename takes the folder out of releases/, so no half-deleted folder can look like a complete release.
    private string? MoveToTombstone(string path)
    {
        if (!Directory.Exists(path)) return null;
        var tombstones = Path.Combine(rootPath, ReleasePaths.TombstoneFolder);
        ReleasePaths.CreateReadableDirectory(tombstones);
        var tombstone = Path.Combine(tombstones, Guid.NewGuid().ToString("N"));
        Directory.Move(path, tombstone);
        return tombstone;
    }

    public Task ActivateAsync(string directoryName, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Release activation needs symlink rename.");
        cancellationToken.ThrowIfCancellationRequested();
        ReleasePaths.RequireDirectoryName(directoryName);
        if (!File.Exists(Path.Combine(ReleasePath(directoryName), PublicationProtocol.ManifestPath)))
            throw new InvalidOperationException("Only a complete release can be activated.");

        var temporary = Path.Combine(rootPath, $".current-{Guid.NewGuid():N}");
        File.CreateSymbolicLink(temporary, $"{ReleasePaths.ReleasesFolder}/{directoryName}");
        if (RenameNative(temporary, CurrentPath) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            File.Delete(temporary);
            throw new Win32Exception(error);
        }

        return Task.CompletedTask;
    }

#pragma warning disable SYSLIB1054
    [DllImport("libc", EntryPoint = "rename", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int RenameNative(string oldPath, string newPath);
#pragma warning restore SYSLIB1054

    public Task<string?> GetActiveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = new FileInfo(CurrentPath).LinkTarget;
        if (target is null) return Task.FromResult<string?>(null);
        var prefix = ReleasePaths.ReleasesFolder + "/";
        if (!target.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("The current release link is malformed.");
        var name = target[prefix.Length..];
        ReleasePaths.RequireDirectoryName(name);
        return Task.FromResult<string?>(name);
    }

    private string ReleasePath(string directoryName) =>
        Path.Combine(rootPath, ReleasePaths.ReleasesFolder, directoryName);
}
