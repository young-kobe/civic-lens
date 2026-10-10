namespace CivicLens.Host.Documents;

public static class EvidenceLinks
{
    public static string Extraction(string extractionId) => "/Documents/Extraction?extractionId=" + Uri.EscapeDataString(extractionId);

    public static string History(string sourceId, string requestedUrl) =>
        $"/Documents/History?sourceId={Uri.EscapeDataString(sourceId)}&requestedUrl={Uri.EscapeDataString(requestedUrl)}";
}
