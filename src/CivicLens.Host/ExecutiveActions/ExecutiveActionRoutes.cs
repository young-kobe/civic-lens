namespace CivicLens.Host.ExecutiveActions;

internal static class ExecutiveActionRoutes
{
    public static string Editor(string recordId, string? status = null) =>
        $"/Review/ExecutiveActions/{recordId}" + (status is null ? "" : "?status=" + status);
}
