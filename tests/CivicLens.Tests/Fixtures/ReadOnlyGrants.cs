using System.Text.RegularExpressions;

namespace CivicLens.Tests.Fixtures;

internal static partial class ReadOnlyGrants
{
    // The read-only role hides a column by name, so each column that holds a reviewer or a note must match this pattern.
    public static Regex HiddenColumnPattern()
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
