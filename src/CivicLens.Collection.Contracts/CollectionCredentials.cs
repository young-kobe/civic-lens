namespace CivicLens.Collection.Contracts;

public static class CollectionCredentials
{
    public const string CongressGov = "congressGov";

    private static readonly Dictionary<string, string> Variables = new(StringComparer.Ordinal)
    {
        [CongressGov] = "CIVIC_LENS_CONGRESS_API_KEY"
    };

    public static bool IsKnown(string name) => Variables.ContainsKey(name);

    public static string VariableOf(string name) => Variables[name];
}
