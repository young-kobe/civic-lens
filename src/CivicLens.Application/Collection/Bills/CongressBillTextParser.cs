using System.Text;
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

    public static string ReadHtml(ReadOnlySpan<byte> body)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(body);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Bill text is not valid UTF-8.", exception);
        }
    }
}
