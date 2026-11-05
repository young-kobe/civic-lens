namespace CivicLens.Host.Spending;

internal static class SpendingRoutes
{
    public const string Page = "/Review/Spending";

    public static string Saved(string what) => $"{Page}?status={what}";
}
