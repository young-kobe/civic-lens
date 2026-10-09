using System.ComponentModel;
using System.Runtime.InteropServices;
using CivicLens.Application.Publication;

namespace CivicLens.Infrastructure.Publication;

internal sealed class FileReleaseStaging(string rootPath, string stagingPath) : IReleaseStaging
{
    private bool completed;

    public async Task WriteFileAsync(string relativePath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(completed, this);
        var path = PrepareTargetPath(relativePath);
        await using (var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Options = FileOptions.Asynchronous
        }))
        {
            await stream.WriteAsync(content, cancellationToken);
        }

        ReleasePaths.MakeReadable(path, isDirectory: false);
    }

    public Task LinkFileAsync(string sourceDirectoryName, string relativePath, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Linking release files needs hard links.");
        ObjectDisposedException.ThrowIf(completed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var source = ResolveSource(sourceDirectoryName, relativePath);
        var target = PrepareTargetPath(relativePath);
        if (File.Exists(target)) throw new IOException("Staged release file already exists.");
        if (LinkNative(source, target) != 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        return Task.CompletedTask;
    }

#pragma warning disable SYSLIB1054
    [DllImport("libc", EntryPoint = "link", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int LinkNative(string existingPath, string newPath);
#pragma warning restore SYSLIB1054

    public Task<string> CompleteAsync(int releaseNumber, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(completed, this);
        ArgumentOutOfRangeException.ThrowIfLessThan(releaseNumber, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(releaseNumber, 999_999);
        cancellationToken.ThrowIfCancellationRequested();
        var releases = Path.Combine(rootPath, ReleasePaths.ReleasesFolder);
        ReleasePaths.CreateReadableDirectory(releases);
        var name = ReleasePaths.ReleaseName(releaseNumber);
        Directory.Move(stagingPath, Path.Combine(releases, name));
        completed = true;
        return Task.FromResult(name);
    }

    private string ResolveSource(string sourceDirectoryName, string relativePath)
    {
        string source;
        try
        {
            source = ReleasePaths.ResolveWithoutLinks(rootPath, sourceDirectoryName, relativePath);
        }
        catch (FileNotFoundException exception)
        {
            throw new InvalidDataException("Linked release file was not found in the base release.", exception);
        }

        if (!File.Exists(source)) throw new InvalidDataException("Linked release path is not a file.");
        return source;
    }

    private string PrepareTargetPath(string relativePath)
    {
        var segments = ReleasePaths.SplitRelativePath(relativePath);
        var folder = stagingPath;
        foreach (var segment in segments[..^1])
        {
            folder = Path.Combine(folder, segment);
            ReleasePaths.CreateReadableDirectory(folder);
        }

        return Path.Combine(folder, segments[^1]);
    }

    public ValueTask DisposeAsync()
    {
        if (completed) return ValueTask.CompletedTask;
        completed = true;
        if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true);
        return ValueTask.CompletedTask;
    }
}
