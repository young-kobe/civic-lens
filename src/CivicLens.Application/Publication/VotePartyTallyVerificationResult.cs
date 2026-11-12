namespace CivicLens.Application.Publication;

/// <summary>
/// <c>Verified</c> revisions have stored groups that equal the party tallies of their kept vote file. <c>Unavailable</c> revisions carry marker zero and no rows.
/// <c>Unresolved</c> revisions carry no marker and no rows, so their count stays unknown. The two <c>WithoutKeptFile</c> counts hold revisions with a positive marker and a matching
/// row count whose file no kept folder holds, so their groups stay unproven. A zero marker on an open range with no file is an open miss too. An open range always has its file
/// in the active folder, so an open miss is a fault.
/// Retention prunes the folders of a closed range, so a closed miss is normal. <c>Failed</c> counts revisions with at least one failure.
/// </summary>
public sealed record VotePartyTallyVerificationResult(int Examined, int Verified, int Unavailable, int Unresolved, int OpenWithoutKeptFile, int ClosedWithoutKeptFile, int Failed,
    IReadOnlyList<VotePartyTallyFailure> Failures, string? NextCursor);
