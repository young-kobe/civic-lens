namespace CivicLens.Host.Review;

internal static class ActionRoutes
{
    public const string List = "/Review/Actions";

    public static string Editor(string recordId, string? status = null) =>
        $"{List}/{recordId}" + (status is null ? "" : "?status=" + status);
}
