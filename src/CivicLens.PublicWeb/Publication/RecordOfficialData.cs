namespace CivicLens.PublicWeb.Publication;

/// <summary>An official that a record page names, with the name that the active release holds. A statement page also gives the seat and the party
/// of the term that holds the statement date. Null means that no single term gives them, or that the page does not show them.</summary>
public sealed record RecordOfficialData(string OfficialId, string Name)
{
    public string? Seat { get; init; }
    public string? Party { get; init; }

    /// <summary>The seat and the party as one line, such as "Senator for AZ, Democrat". Null when the term gives neither.</summary>
    public string? Role => Seat is null && Party is null ? null : string.Join(", ", new[] { Seat, Party }.OfType<string>());
}
