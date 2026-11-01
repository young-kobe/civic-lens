using CivicLens.Application.Documents;

namespace CivicLens.Application.Collection.Bills;

public static class CongressBillTextParser
{
    private const string Version = "congress-bill-text-v1";

    public static string ExtractionVersion(IDocumentTextExtractor extractor)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        return $"{Version}+{extractor.ExtractionVersion}";
    }
}
