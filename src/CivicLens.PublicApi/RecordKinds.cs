using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi;

/// <summary>The text form of a record kind in the database and the API. It equals the form in the release manifest.</summary>
public static class RecordKinds
{
    private static readonly Dictionary<string, PublishedRecordKind> ByText =
        Enum.GetValues<PublishedRecordKind>().ToDictionary(PublicationProtocol.KindQueryValue, StringComparer.Ordinal);

    public static bool TryParse(string? text, out PublishedRecordKind kind)
    {
        kind = default;
        return text is not null && ByText.TryGetValue(text, out kind);
    }
}
