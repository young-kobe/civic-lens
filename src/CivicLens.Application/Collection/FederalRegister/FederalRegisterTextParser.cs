using CivicLens.Application.Documents;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Collection.FederalRegister;

/// <summary>Owns the extraction version of document body text and the rule that keeps printed page markers out of it.</summary>
public static class FederalRegisterTextParser
{
    private const string Version = "federal-register-text-v1";

    /// <summary>The body of a document holds page markers (<c>printed-page-inline</c> and <c>printed-page-details</c>) that are not text of the order.
    /// Both carry the class <c>unprinted-element</c>.</summary>
    public static DocumentContentProfile Profile { get; } = new("federal-register-text", "body", [".unprinted-element"]);

    public static string ExtractionVersion(IDocumentTextExtractor extractor)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        return $"{Version}+{extractor.ExtractionVersion}";
    }
}
