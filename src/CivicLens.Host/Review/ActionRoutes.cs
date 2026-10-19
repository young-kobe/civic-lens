namespace CivicLens.Host.Review;

internal static class ActionRoutes
{
    public const string List = "/Review/Actions";
    public const string Triage = List + "/Triage";

    public static string Editor(string recordId, string? status = null) =>
        $"{List}/{recordId}" + (status is null ? "" : "?status=" + status);
}
