namespace CivicLens.Host.Documents;

internal static class DocumentRoutes
{
    public static string Editor(string draftId, string? status = null) =>
        $"/Review/{draftId}" + (status is null ? "" : "?status=" + status);
}
