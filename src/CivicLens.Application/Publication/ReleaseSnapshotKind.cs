namespace CivicLens.Application.Publication;

/// <summary>Names one kind of release snapshot that the owner commands convert and verify, so failure reasons and texts say which snapshot failed.</summary>
public sealed record ReleaseSnapshotKind(string ReasonPrefix, string Label)
{
    public static readonly ReleaseSnapshotKind BillCounts = new("counts", "bill count");

    public static readonly ReleaseSnapshotKind PartyLineUps = new("lineUps", "party line-up");
}
