using System.Text.RegularExpressions;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Infrastructure.Publication;

public sealed class PublishedRecordsInsertOnlyTests
{
    [Fact]
    public void NoProductionCodeUpdatesOrDeletesTheFirstPublicationTimesAndOnlyTheInsertTouchesTheTable()
    {
        var source = Path.Combine(RepositoryRoot.FullName, "src");
        var files = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToDictionary(path => Path.GetRelativePath(source, path).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllText);

        var writes = new Regex(@"\b(UPDATE|DELETE\s+FROM|TRUNCATE(\s+TABLE)?)\s+(ONLY\s+)?(public\.)?published_records\b", RegexOptions.IgnoreCase);
        Assert.Empty(files.Where(file => writes.IsMatch(file.Value)).Select(file => file.Key));
        Assert.Equal(["CivicLens.Infrastructure/Publication/Persistence/PublicationWithdrawalRowConfiguration.cs",
            "CivicLens.Infrastructure/Publication/Persistence/PublishedEntryRowConfiguration.cs",
            "CivicLens.Infrastructure/Publication/Persistence/PublishedRecordRow.cs",
            "CivicLens.Infrastructure/Publication/Persistence/PublishedRecordRowConfiguration.cs"], files.Where(file => Regex.IsMatch(file.Value, @"\bPublishedRecordRow\b")).Select(file => file.Key).Order());
        Assert.Equal(["CivicLens.Infrastructure/Publication/PublishedRecords.cs"], files.Where(file => Regex.IsMatch(file.Value, @"\bpublished_records\b")
            && !file.Key.Contains("/PublishedRecordRow", StringComparison.Ordinal)).Select(file => file.Key));
    }
}
