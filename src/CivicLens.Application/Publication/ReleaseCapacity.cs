namespace CivicLens.Application.Publication;

/// <summary>The records and withdrawals of the active release, and the approved records that the cap leaves out of it.
/// Held is zero when no approved record waits, because the manifest is then not read.</summary>
public sealed record ReleaseCapacity(int Held, int WaitingForRoom);
