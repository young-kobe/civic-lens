namespace CivicLens.Host.Statements;

internal static class GateLabelRoutes
{
    public const string List = "/Review/GateLabels";
    public const string AccessDenied = "/access-denied";

    public static string Editor(string candidateId, string? status = null) =>
        $"{List}/{candidateId}" + (status is null ? "" : "?status=" + status);
}
