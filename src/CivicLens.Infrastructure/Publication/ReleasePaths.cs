using System.Text.RegularExpressions;

namespace CivicLens.Infrastructure.Publication;

internal static partial class ReleasePaths
{
    public const string StagingFolder = "staging";
    public const string ReleasesFolder = "releases";
    public const string CurrentLink = "current";

    private const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
    private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite |
        UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    public static string ReleaseName(int releaseNumber) => $"{releaseNumber:D6}-{Guid.NewGuid():N}";

    public static void RequireDirectoryName(string directoryName)
    {
        if (directoryName is null || !DirectoryNamePattern().IsMatch(directoryName))
            throw new ArgumentException("Release directory name is invalid.", nameof(directoryName));
    }

    public static string[] SplitRelativePath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath) || relativePath.Length > 256 || Path.IsPathRooted(relativePath))
            throw new ArgumentException("Release path must be a short relative path.", nameof(relativePath));
        var segments = relativePath.Split('/');
        if (segments.Any(segment => !SegmentPattern().IsMatch(segment) || segment is "." or ".."))
            throw new ArgumentException("Release path has an invalid segment.", nameof(relativePath));
        return segments;
    }

    public static string ResolveWithoutLinks(string rootPath, string directoryName, string relativePath)
    {
        RequireDirectoryName(directoryName);
        var segments = SplitRelativePath(relativePath);
        var path = Path.Combine(rootPath, ReleasesFolder, directoryName);
        RejectLink(path);
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
            RejectLink(path);
        }

        return path;
    }

    public static void MakeReadable(string path, bool isDirectory)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path, isDirectory ? DirectoryMode : FileMode);
    }

    public static void CreateReadableDirectory(string path)
    {
        if (Directory.Exists(path)) return;
        Directory.CreateDirectory(path);
        MakeReadable(path, isDirectory: true);
    }

    private static void RejectLink(string path)
    {
        var info = new FileInfo(path);
        if (info.LinkTarget is not null) throw new InvalidDataException("Release content must not contain symlinks.");
        if (!info.Exists && !Directory.Exists(path)) throw new FileNotFoundException("Release file was not found.", path);
    }

    [GeneratedRegex("^[0-9]{6}-[0-9a-f]{32}$")]
    private static partial Regex DirectoryNamePattern();

    [GeneratedRegex("^[a-z0-9._-]{1,64}$")]
    private static partial Regex SegmentPattern();
}
