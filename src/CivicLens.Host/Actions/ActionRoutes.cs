namespace CivicLens.Host.Actions;

internal static class ActionRoutes
{
    public static string Editor(string recordId, string? status = null) =>
        $"/Review/Actions/{recordId}" + (status is null ? "" : "?status=" + status);
}
