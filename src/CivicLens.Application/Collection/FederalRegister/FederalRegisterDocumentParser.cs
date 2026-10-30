using System.Text.Json.Serialization;
using CivicLens.Core.ExecutiveActions;

namespace CivicLens.Application.Collection.FederalRegister;

public static class FederalRegisterDocumentParser
{
    public const string ExtractionVersion = "federal-register-document-v1";
    private const string PresidentialDocumentType = "Presidential Document";

    private sealed record Wire(
        [property: JsonPropertyName("document_number")] string? DocumentNumber,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("subtype")] string? Subtype,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("signing_date")] DateOnly? SigningDate,
        [property: JsonPropertyName("publication_date")] DateOnly? PublicationDate,
        [property: JsonPropertyName("citation")] string? Citation,
        [property: JsonPropertyName("president")] WirePresident? President,
        [property: JsonPropertyName("correction_of")] string? CorrectionOf,
        [property: JsonPropertyName("presidential_document_number")] string? PresidentialDocumentNumber,
        [property: JsonPropertyName("html_url")] string? HtmlUrl);

    private sealed record WirePresident(
        [property: JsonPropertyName("identifier")] string? Identifier,
        [property: JsonPropertyName("name")] string? Name);

    public static FederalRegisterDocument Parse(ReadOnlySpan<byte> body, string expectedNumber)
    {
        var wire = CongressApiJson.Deserialize<Wire>(body, "Federal Register document");
        if (wire.DocumentNumber != expectedNumber || wire.Type != PresidentialDocumentType)
            throw new InvalidDataException("Federal Register response is not the requested presidential document.");
        var kind = ExecutiveActionKinds.TryRead(wire.Subtype)
            ?? throw new InvalidDataException("Federal Register document has a type that Civic Lens does not record.");
        var president = wire.President;
        return new FederalRegisterDocument(expectedNumber, CorrectedNumberOf(wire.CorrectionOf), kind, Optional(wire.PresidentialDocumentNumber),
            Required(wire.Title), wire.SigningDate ?? throw new InvalidDataException("Federal Register document needs a signing date."),
            wire.PublicationDate ?? throw new InvalidDataException("Federal Register document needs a publication date."),
            Required(wire.Citation), Required(president?.Identifier), Required(president?.Name), RequiredUrl(wire.HtmlUrl));
    }

    private static string? CorrectedNumberOf(string? correctionOf) =>
        correctionOf is null ? null : FederalRegisterDocumentNumber.Require(correctionOf[(correctionOf.LastIndexOf('/') + 1)..]);

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : RenderedText.SingleLine(value);

    private static string Required(string? value) =>
        Optional(value) ?? throw new InvalidDataException("Federal Register document is missing a required field.");

    private static string RequiredUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "www.federalregister.gov"
            ? uri.AbsoluteUri
            : throw new InvalidDataException("Federal Register document needs an https link on federalregister.gov.");
}
