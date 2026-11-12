namespace CivicLens.Application.Publication;

/// <summary>What one release commit did. A release that expects no rows and holds none is not the same as a release that already holds its exact rows.</summary>
public enum ReleaseSnapshotWrite
{
    Added,
    AlreadyPresent,
    NothingExpected
}
