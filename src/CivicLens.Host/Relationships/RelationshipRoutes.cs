namespace CivicLens.Host.Relationships;

internal static class RelationshipRoutes
{
    public const string List = "/Review/Relationships";

    public static string Editor(string recordId, string? status = null) =>
        $"{List}/{recordId}" + (status is null ? "" : "?status=" + status);
}
