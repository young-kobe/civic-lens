namespace CivicLens.Application.Collection.FederalRegister;

public static class FederalRegisterJobKeys
{
    public static string Document(string sourceId, string documentNumber) => $"federal-register/{sourceId}/document/{documentNumber}";

    /// <summary>Refetches the detail of a stored document that has no pinned body URL. Its admission only fills that URL.</summary>
    public static string BodyUrl(string sourceId, string documentNumber) => Document(sourceId, documentNumber) + BodyUrlSuffix;

    public const string BodyUrlSuffix = "/body-url";

    public static bool IsBodyUrl(string idempotencyKey) => idempotencyKey.EndsWith(BodyUrlSuffix, StringComparison.Ordinal);

    public static string Text(string sourceId, string documentNumber) => $"federal-register/{sourceId}/text/{documentNumber}";
}
