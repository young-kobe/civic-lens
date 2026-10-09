using CivicLens.Application.Publication;

namespace CivicLens.Infrastructure.Publication;

internal sealed class FileReleaseStaging(string rootPath, string stagingPath) : IReleaseStaging
{
    private bool completed;

    public async Task WriteFileAsync(string relativePath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(completed, this);
        var segments = ReleasePaths.SplitRelativePath(relativePath);
        var folder = stagingPath;
        foreach (var segment in segments[..^1])
        {
            folder = Path.Combine(folder, segment);
            ReleasePaths.CreateReadableDirectory(folder);
        }

        var path = Path.Combine(folder, segments[^1]);
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

    public ValueTask DisposeAsync()
    {
        if (completed) return ValueTask.CompletedTask;
        completed = true;
        if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true);
        return ValueTask.CompletedTask;
    }
}
