namespace CivicLens.Host.Relationships;

internal static class RelationshipRoutes
{
    private const string Base = "/Review/Relationships";

    public static string Editor(string recordId, string? status = null) =>
        $"{Base}/{recordId}" + (status is null ? "" : "?status=" + status);
}
