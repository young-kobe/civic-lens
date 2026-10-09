using System.Text;
using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Publication;

namespace CivicLens.Tests.Infrastructure.Publication;

public sealed class FileReleaseDirectoryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "civic-release-" + Guid.NewGuid().ToString("N"));
    private readonly FileReleaseDirectory releases;

    public FileReleaseDirectoryTests() => releases = new FileReleaseDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void RootMustBeAbsoluteSoTheLinkTargetCannotDependOnTheWorkingDirectory()
    {
        Assert.Throws<ArgumentException>(() => new FileReleaseDirectory("relative/root"));
    }

    [Fact]
    public async Task ActivationPointsARelativeLinkAtTheReleaseSoTheRootCanMoveWithoutBreakingIt()
    {
        var name = await BuildReleaseAsync(1, "one");
        await releases.ActivateAsync(name, default);

        Assert.Equal($"releases/{name}", new FileInfo(Path.Combine(root, "current")).LinkTarget);
        Assert.Equal(name, await releases.GetActiveAsync(default));
        var moved = root + "-moved";
        Directory.Move(root, moved);
        try
        {
            Assert.Equal("one", File.ReadAllText(Path.Combine(moved, "current", "release.json")));
        }
        finally
        {
            Directory.Move(moved, root);
        }
    }

    [Fact]
    public async Task SwappingTheActiveReleaseNeverLeavesReadersWithoutACurrentRelease()
    {
        var first = await BuildReleaseAsync(1, "one");
        var second = await BuildReleaseAsync(2, "two");
        await releases.ActivateAsync(first, default);
        using var stop = new CancellationTokenSource();
        var failures = 0;
        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                try { _ = File.ReadAllText(Path.Combine(root, "current", "release.json")); }
                catch (IOException) { Interlocked.Increment(ref failures); }
            }
        });

        for (var swap = 0; swap < 300; swap++) await releases.ActivateAsync(swap % 2 == 0 ? second : first, default);
        await stop.CancelAsync();
        await reader;

        Assert.Equal(0, failures);
    }

    [Fact]
    public async Task OnlyACompleteReleaseCanBeActivated()
    {
        await using var staging = await releases.BeginAsync(default);
        await staging.WriteFileAsync("index.html", Encoding.UTF8.GetBytes("x"), default);
        var name = await staging.CompleteAsync(1, default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => releases.ActivateAsync(name, default));
        Assert.Null(await releases.GetActiveAsync(default));
    }

    [Theory]
    [InlineData("../escape.json")]
    [InlineData("a/../../escape.json")]
    [InlineData("/etc/passwd")]
    [InlineData("./a.json")]
    [InlineData("a//b.json")]
    [InlineData("Upper.json")]
    [InlineData("a b.json")]
    [InlineData("")]
    public async Task StagingRejectsPathsThatCouldEscapeTheRelease(string relativePath)
    {
        await using var staging = await releases.BeginAsync(default);

        await Assert.ThrowsAsync<ArgumentException>(() => staging.WriteFileAsync(relativePath, new byte[] { 1 }, default));
        Assert.False(File.Exists(Path.Combine(root, "escape.json")));
    }

    [Theory]
    [InlineData("../releases")]
    [InlineData("000001-short")]
    [InlineData("current")]
    public async Task ReadsAndDeletesRejectMalformedDirectoryNames(string directoryName)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => releases.ReadFileAsync(directoryName, "release.json", 10, default));
        await Assert.ThrowsAsync<ArgumentException>(() => releases.DeleteAsync(directoryName, default));
        await Assert.ThrowsAsync<ArgumentException>(() => releases.ActivateAsync(directoryName, default));
    }

    [Fact]
    public async Task ReadingRejectsSymlinksSoAReleaseCannotExposeFilesOutsideItself()
    {
        var name = await BuildReleaseAsync(1, "one");
        var outside = Path.Combine(root, "secret.txt");
        File.WriteAllText(outside, "secret");
        File.CreateSymbolicLink(Path.Combine(root, "releases", name, "leak.txt"), outside);

        await Assert.ThrowsAsync<InvalidDataException>(() => releases.ReadFileAsync(name, "leak.txt", 100, default));
    }

    [Fact]
    public async Task ReadingEnforcesTheMaximumSize()
    {
        var name = await BuildReleaseAsync(1, "0123456789");

        Assert.Equal(10, (await releases.ReadFileAsync(name, "release.json", 10, default)).Length);
        await Assert.ThrowsAsync<InvalidDataException>(() => releases.ReadFileAsync(name, "release.json", 9, default));
    }

    [Fact]
    public async Task StagingNeverOverwritesAnExistingFile()
    {
        await using var staging = await releases.BeginAsync(default);
        await staging.WriteFileAsync("records/a.json", new byte[] { 1 }, default);

        await Assert.ThrowsAsync<IOException>(() => staging.WriteFileAsync("records/a.json", new byte[] { 2 }, default));
    }

    [Fact]
    public async Task AnUncompletedStagingDirectoryIsRemovedSoFailedBuildsLeaveNothingBehind()
    {
        await using (var staging = await releases.BeginAsync(default))
            await staging.WriteFileAsync("records/a.json", new byte[] { 1 }, default);

        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(root, "staging")));
        Assert.False(Directory.Exists(Path.Combine(root, "releases")) &&
            Directory.GetFileSystemEntries(Path.Combine(root, "releases")).Length > 0);
    }

    [Fact]
    public async Task CompletedFilesAreWorldReadableAndDirectoriesTraversableForTheWebServer()
    {
        if (OperatingSystem.IsWindows()) return;
        var name = await BuildReleaseAsync(1, "one");

        var release = Path.Combine(root, "releases", name);
        Assert.True(File.GetUnixFileMode(Path.Combine(release, "release.json")).HasFlag(UnixFileMode.OtherRead));
        Assert.True(File.GetUnixFileMode(release).HasFlag(UnixFileMode.OtherExecute));
        Assert.True(File.GetUnixFileMode(Path.Combine(release, "records")).HasFlag(UnixFileMode.OtherExecute));
    }

    [Fact]
    public async Task DeletingRemovesOnlyTheNamedRelease()
    {
        var first = await BuildReleaseAsync(1, "one");
        var second = await BuildReleaseAsync(2, "two");

        await releases.DeleteAsync(first, default);

        Assert.False(Directory.Exists(Path.Combine(root, "releases", first)));
        Assert.True(Directory.Exists(Path.Combine(root, "releases", second)));
    }

    [Fact]
    public async Task LinkedFilesShareStorageWithTheBaseReleaseSoDiskDoesNotGrowPerRelease()
    {
        if (OperatingSystem.IsWindows()) return;
        var baseName = await BuildReleaseAsync(1, "one");
        await using var staging = await releases.BeginAsync(default);
        await staging.LinkFileAsync(baseName, "records/a.json", default);
        var linkedName = await staging.CompleteAsync(2, default);

        var source = Path.Combine(root, "releases", baseName, "records", "a.json");
        var linked = Path.Combine(root, "releases", linkedName, "records", "a.json");
        Assert.Equal("{}", await File.ReadAllTextAsync(linked));
        Assert.Null(new FileInfo(linked).LinkTarget);
        await File.AppendAllTextAsync(source, "x");
        Assert.Equal("{}x", await File.ReadAllTextAsync(linked));
        Assert.True(File.GetUnixFileMode(linked).HasFlag(UnixFileMode.OtherRead));
    }

    [Theory]
    [InlineData("../escape.json")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    public async Task LinkingRejectsPathsThatCouldEscapeTheRelease(string relativePath)
    {
        var baseName = await BuildReleaseAsync(1, "one");
        await using var staging = await releases.BeginAsync(default);

        await Assert.ThrowsAsync<ArgumentException>(() => staging.LinkFileAsync(baseName, relativePath, default));
    }

    [Theory]
    [InlineData("../releases")]
    [InlineData("current")]
    public async Task LinkingRejectsMalformedSourceDirectoryNames(string directoryName)
    {
        await using var staging = await releases.BeginAsync(default);

        await Assert.ThrowsAsync<ArgumentException>(() => staging.LinkFileAsync(directoryName, "records/a.json", default));
    }

    [Fact]
    public async Task LinkingAMissingSourceIsCorruptBaseDataNotBadInput()
    {
        var baseName = await BuildReleaseAsync(1, "one");
        await using var staging = await releases.BeginAsync(default);

        await Assert.ThrowsAsync<InvalidDataException>(() => staging.LinkFileAsync(baseName, "records/missing.json", default));
        await Assert.ThrowsAsync<InvalidDataException>(() => staging.LinkFileAsync(baseName, "records", default));
    }

    [Fact]
    public async Task LinkingNeverOverwritesAnExistingFile()
    {
        var baseName = await BuildReleaseAsync(1, "one");
        await using var staging = await releases.BeginAsync(default);
        await staging.WriteFileAsync("records/a.json", new byte[] { 2 }, default);

        await Assert.ThrowsAnyAsync<IOException>(() => staging.LinkFileAsync(baseName, "records/a.json", default));
    }

    [Fact]
    public async Task LinkingRejectsSymlinkSourcesSoAReleaseCannotExposeFilesOutsideItself()
    {
        var baseName = await BuildReleaseAsync(1, "one");
        var outside = Path.Combine(root, "secret.txt");
        File.WriteAllText(outside, "secret");
        File.CreateSymbolicLink(Path.Combine(root, "releases", baseName, "leak.txt"), outside);
        await using var staging = await releases.BeginAsync(default);

        await Assert.ThrowsAsync<InvalidDataException>(() => staging.LinkFileAsync(baseName, "leak.txt", default));
    }

    private async Task<string> BuildReleaseAsync(int number, string manifest)
    {
        await using var staging = await releases.BeginAsync(default);
        await staging.WriteFileAsync("release.json", Encoding.UTF8.GetBytes(manifest), default);
        await staging.WriteFileAsync("records/a.json", Encoding.UTF8.GetBytes("{}"), default);
        return await staging.CompleteAsync(number, default);
    }
}
