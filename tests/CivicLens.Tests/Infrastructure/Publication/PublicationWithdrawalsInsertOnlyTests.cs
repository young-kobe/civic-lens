using System.Text.RegularExpressions;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Infrastructure.Publication;

public sealed class PublicationWithdrawalsInsertOnlyTests
{
    private const string Writer = "CivicLens.Infrastructure/Publication/PublicationWithdrawalRows.cs";

    [Fact]
    public void OnlyOneClassWritesTheWithdrawalTablesAndTheOnlyUpdateClosesARange()
    {
        var source = Path.Combine(RepositoryRoot.FullName, "src");
        var files = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToDictionary(path => Path.GetRelativePath(source, path).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllText);

        var deletes = new Regex(@"\b(DELETE\s+FROM|TRUNCATE(\s+TABLE)?)\s+(ONLY\s+)?(public\.)?publication_withdrawal(s|_corrections)\b", RegexOptions.IgnoreCase);
        var corrections = new Regex(@"\bUPDATE\s+(ONLY\s+)?(public\.)?publication_withdrawal_corrections\b", RegexOptions.IgnoreCase);
        var updates = new Regex(@"\bUPDATE\s+(ONLY\s+)?(public\.)?publication_withdrawals\b(?!_)", RegexOptions.IgnoreCase);
        Assert.Empty(files.Where(file => deletes.IsMatch(file.Value) || corrections.IsMatch(file.Value)).Select(file => file.Key));
        Assert.Equal([Writer], files.Where(file => updates.IsMatch(file.Value)).Select(file => file.Key));
        Assert.Matches(@"UPDATE publication_withdrawals w SET last_release = \{0\}\s", files[Writer]);
        Assert.Single(updates.Matches(files[Writer]));
        Assert.Equal([Writer], files.Where(file => Regex.IsMatch(file.Value, @"\bPublicationWithdrawal(Correction)?Row\b")
            && !file.Key.Contains("/Persistence/", StringComparison.Ordinal)).Select(file => file.Key));
    }
}
