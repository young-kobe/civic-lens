using System.Text.RegularExpressions;

namespace CivicLens.Tests.Fixtures;

internal static partial class ReadOnlyGrants
{
    /// <summary>The column name pattern that deploy/scripts/readonly-grants.sql hides from the read-only role.</summary>
    public static Regex HiddenColumn()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "deploy", "scripts", "readonly-grants.sql"));
        var literals = HiddenColumnAssignment().Match(script).Groups["value"].Value;
        return new Regex(string.Concat(QuotedLiteral().Matches(literals).Select(match => match.Groups[1].Value)));
    }

    [GeneratedRegex(@"hidden_column constant text :=(?<value>.*?);", RegexOptions.Singleline)]
    private static partial Regex HiddenColumnAssignment();

    [GeneratedRegex("'([^']*)'")]
    private static partial Regex QuotedLiteral();
}
