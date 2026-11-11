namespace CivicLens.Application.Publication;

/// <summary>
/// <c>Verified</c> revisions have stored groups that equal the groups rebuilt from their member list. <c>Unavailable</c> revisions carry marker zero and no rows.
/// <c>Unresolved</c> revisions carry no marker and no rows, so their count stays unknown. <c>Failed</c> counts revisions with at least one failure.
/// </summary>
public sealed record VotePartyTallyVerificationResult(int Examined, int Verified, int Unavailable, int Unresolved, int Failed,
    IReadOnlyList<VotePartyTallyFailure> Failures, string? NextCursor);

public sealed record VotePartyTallyFailure(string RecordId, int RevisionNumber, string Reason, string Detail)
{
    public const string MarkerWithoutRows = "markerDiffersFromRows";
    public const string RowsWithoutMarker = "rowsWithoutMarker";
    public const string MemberPartyMissing = "memberPartyMissing";
    public const string MissingGroup = "missingGroup";
    public const string ExtraGroup = "extraGroup";
    public const string DifferentCounts = "differentCounts";
}
