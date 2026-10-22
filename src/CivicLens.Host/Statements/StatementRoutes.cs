namespace CivicLens.Host.Statements;

internal static class StatementRoutes
{
    public const string List = "/Review/Statements";
    public const string Triage = List + "/Triage";

    public static string Editor(string candidateId, string? status = null) =>
        $"{List}/{candidateId}" + (status is null ? "" : "?status=" + status);
}
