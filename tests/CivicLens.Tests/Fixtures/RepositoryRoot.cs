namespace CivicLens.Tests.Fixtures;

internal static class RepositoryRoot
{
    public static string FullName { get; } = Find();

    private static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CivicLens.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Cannot locate CivicLens.slnx.");
    }
}
