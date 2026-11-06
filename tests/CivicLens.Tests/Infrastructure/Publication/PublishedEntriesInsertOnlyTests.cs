using System.Text.RegularExpressions;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Infrastructure.Publication;

public sealed class PublishedEntriesInsertOnlyTests
{
    [Fact]
    public void OnlyTheWriterTouchesTheEntryTablesAndItsOnlyUpdatesFillANullBillLineAndANullCongress()
    {
        var source = Path.Combine(RepositoryRoot.FullName, "src");
        var files = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToDictionary(path => Path.GetRelativePath(source, path).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllText);

        const string writer = "CivicLens.Infrastructure/Publication/PublishedEntries.cs";
        var writes = new Regex(@"\b(UPDATE|DELETE\s+FROM|TRUNCATE(\s+TABLE)?)\s+(ONLY\s+)?(public\.)?published_entr(ies|y_\w+)\b(\s+e\s+SET\s+\w+)?", RegexOptions.IgnoreCase);
        var found = files.SelectMany(file => writes.Matches(file.Value).Select(match => (file.Key, match.Value))).ToList();
        Assert.Equal([(writer, "UPDATE published_entries e SET bill_line"), (writer, "UPDATE published_entries e SET congress")], found);
        var congress = Regex.Match(files[writer], "FillCongresses = \"\"\"(.*?)\"\"\"", RegexOptions.Singleline).Groups[1].Value;
        Assert.Contains("AND e.congress IS NULL", congress);
        var fill = Regex.Match(files[writer], "FillBillLines = \"\"\"(.*?)\"\"\"", RegexOptions.Singleline).Groups[1].Value;
        Assert.Matches(@"SET bill_line = locked\.bill_line\s+FROM locked", fill);
        Assert.Contains("WHERE e.bill_line IS NULL", fill);
        Assert.Matches(@"ORDER BY e\.kind, e\.record_id, e\.revision_number\s+FOR UPDATE OF e", fill);
        // The grants file names the table once, to give the public role a read grant on its columns.
        Assert.Equal(["CivicLens.Infrastructure/Publication/PublicApiGrants.cs", writer], files.Where(file => Regex.IsMatch(file.Value, @"\bpublished_entr(ies|y_\w+)\b")
            && !file.Key.Contains("/Persistence/", StringComparison.Ordinal)).Select(file => file.Key).Order(StringComparer.Ordinal));
        Assert.Equal([writer], files.Where(file => Regex.IsMatch(file.Value, @"\bPublishedEntry\w*Row\b")
            && !file.Key.Contains("/Persistence/", StringComparison.Ordinal)).Select(file => file.Key));
    }
}
