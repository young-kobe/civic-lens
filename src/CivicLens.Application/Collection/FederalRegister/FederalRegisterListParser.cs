using System.Text.Json.Serialization;

namespace CivicLens.Application.Collection.FederalRegister;

public static class FederalRegisterListParser
{
    private sealed record Wire(
        [property: JsonPropertyName("count")] int? Count,
        [property: JsonPropertyName("total_pages")] int? TotalPages,
        [property: JsonPropertyName("results")] WireItem[]? Results);

    private sealed record WireItem(
        [property: JsonPropertyName("document_number")] string? DocumentNumber,
        [property: JsonPropertyName("publication_date")] DateOnly? PublicationDate);

    public static FederalRegisterListPage Parse(ReadOnlySpan<byte> body)
    {
        var wire = CongressApiJson.Deserialize<Wire>(body, "Federal Register list");
        if (wire.Results is null || wire.Count is not >= 0 || wire.TotalPages is not >= 0)
            throw new InvalidDataException("Federal Register list needs a count, a page count, and results.");
        if (wire.TotalPages > 1 || wire.Results.Length != wire.Count || wire.Results.Length > FederalRegisterUrl.PageLimit)
            throw new InvalidDataException("Federal Register list does not fit one page.");
        var items = wire.Results.Select(item => new FederalRegisterListItem(
            FederalRegisterDocumentNumber.Require(item.DocumentNumber),
            item.PublicationDate ?? throw new InvalidDataException("Federal Register list item needs a publication date."))).ToArray();
        if (items.DistinctBy(item => item.DocumentNumber).Count() != items.Length)
            throw new InvalidDataException("Federal Register list repeats a document.");
        return new FederalRegisterListPage(items);
    }
}
