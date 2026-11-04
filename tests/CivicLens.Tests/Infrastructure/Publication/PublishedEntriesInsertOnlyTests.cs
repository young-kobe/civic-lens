using System.Text.RegularExpressions;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Infrastructure.Publication;

public sealed class PublishedEntriesInsertOnlyTests
{
    [Fact]
    public void NoProductionCodeUpdatesOrDeletesAnEntryRowAndOnlyTheWriterTouchesTheTables()
    {
        var source = Path.Combine(RepositoryRoot.FullName, "src");
        var files = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToDictionary(path => Path.GetRelativePath(source, path).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllText);

        var writes = new Regex(@"\b(UPDATE|DELETE\s+FROM|TRUNCATE(\s+TABLE)?)\s+(ONLY\s+)?(public\.)?published_entr(ies|y_\w+)\b", RegexOptions.IgnoreCase);
        Assert.Empty(files.Where(file => writes.IsMatch(file.Value)).Select(file => file.Key));
        Assert.Equal(["CivicLens.Infrastructure/Publication/PublishedEntries.cs"], files.Where(file => Regex.IsMatch(file.Value, @"\bpublished_entr(ies|y_\w+)\b")
            && !file.Key.Contains("/Persistence/", StringComparison.Ordinal)).Select(file => file.Key));
        Assert.Equal(["CivicLens.Infrastructure/Publication/PublishedEntries.cs"], files.Where(file => Regex.IsMatch(file.Value, @"\bPublishedEntry\w*Row\b")
            && !file.Key.Contains("/Persistence/", StringComparison.Ordinal)).Select(file => file.Key));
    }
}
