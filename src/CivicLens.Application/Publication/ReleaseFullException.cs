using System.Collections.Immutable;

namespace CivicLens.Application.Publication;

/// <summary>A release that would pass the record cap. A replacement or a return adds no entry, so only the new records need room.</summary>
public sealed class ReleaseFullException(int freeSlots, ImmutableArray<PublishedRecordReference> newRecords)
    : ArgumentException("The release would exceed the maximum record count.", "request")
{
    public int FreeSlots { get; } = freeSlots;

    public ImmutableArray<PublishedRecordReference> NewRecords { get; } = newRecords;
}
