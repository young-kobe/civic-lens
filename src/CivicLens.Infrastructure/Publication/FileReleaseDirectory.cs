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

    public Task DeleteAsync(string directoryName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReleasePaths.RequireDirectoryName(directoryName);
        var path = ReleasePath(directoryName);
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        return Task.CompletedTask;
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
