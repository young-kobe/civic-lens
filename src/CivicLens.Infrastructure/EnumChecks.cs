namespace CivicLens.Infrastructure;

/// <summary>Owns the SQL text of the check that a column holds one of the names of an enum.</summary>
internal static class EnumChecks
{
    public static string InList(string column, IEnumerable<string> names) => $"{column} IN ({string.Join(", ", names.Select(name => $"'{name}'"))})";
}
