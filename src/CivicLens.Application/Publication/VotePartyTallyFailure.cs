namespace CivicLens.Application.Publication;

public sealed record VotePartyTallyFailure(string RecordId, int RevisionNumber, string Reason, string Detail)
{
    public const string MarkerWithoutRows = "markerDiffersFromRows";
    public const string MarkerDiffersFromFile = "markerDiffersFromFile";
    public const string RowsWithoutMarker = "rowsWithoutMarker";
    public const string MissingGroup = "missingGroup";
    public const string ExtraGroup = "extraGroup";
    public const string DifferentCounts = "differentCounts";
}
