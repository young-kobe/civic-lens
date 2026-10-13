namespace CivicLens.Host.Review;

internal static class StatementRoutes
{
    public const string List = "/Review/Statements";

    public static string Editor(string candidateId, string? status = null) =>
        $"{List}/{candidateId}" + (status is null ? "" : "?status=" + status);
}
