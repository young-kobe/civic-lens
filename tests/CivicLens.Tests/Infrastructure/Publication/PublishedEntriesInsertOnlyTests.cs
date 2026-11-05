using System.Text.RegularExpressions;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Infrastructure.Publication;

public sealed class PublishedEntriesInsertOnlyTests
{
    [Fact]
    public void OnlyTheWriterTouchesTheEntryTablesAndItsOnlyUpdateFillsANullBillLine()
    {
        var source = Path.Combine(RepositoryRoot.FullName, "src");
        var files = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToDictionary(path => Path.GetRelativePath(source, path).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllText);

        const string writer = "CivicLens.Infrastructure/Publication/PublishedEntries.cs";
        var writes = new Regex(@"\b(UPDATE|DELETE\s+FROM|TRUNCATE(\s+TABLE)?)\s+(ONLY\s+)?(public\.)?published_entr(ies|y_\w+)\b(\s+e\s+SET\s+\w+)?", RegexOptions.IgnoreCase);
        var found = files.SelectMany(file => writes.Matches(file.Value).Select(match => (file.Key, match.Value))).ToList();
        Assert.Equal([(writer, "UPDATE published_entries e SET bill_line")], found);
        var fill = Regex.Match(files[writer], "FillBillLines = \"\"\"(.*?)\"\"\"", RegexOptions.Singleline).Groups[1].Value;
        Assert.Matches(@"SET bill_line = k\.bill_line\s+FROM", fill);
        Assert.Contains("AND e.bill_line IS NULL", fill);
        // The grants file names the table once, to give the public role a read grant on its columns.
        Assert.Equal(["CivicLens.Infrastructure/Publication/PublicApiGrants.cs", writer], files.Where(file => Regex.IsMatch(file.Value, @"\bpublished_entr(ies|y_\w+)\b")
            && !file.Key.Contains("/Persistence/", StringComparison.Ordinal)).Select(file => file.Key).Order(StringComparer.Ordinal));
        Assert.Equal([writer], files.Where(file => Regex.IsMatch(file.Value, @"\bPublishedEntry\w*Row\b")
            && !file.Key.Contains("/Persistence/", StringComparison.Ordinal)).Select(file => file.Key));
    }
}
