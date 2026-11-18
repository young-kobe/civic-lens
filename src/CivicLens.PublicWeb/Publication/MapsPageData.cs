using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The selected map and its form choices from one published snapshot.</summary>
public sealed record MapsPageData(int Release, int? Congress, int? ReleaseCongress, PublishedChamber Chamber, string Layer,
    DateOnly? AsOf, string? IssueName, IReadOnlyList<MapMemberData> Members, IReadOnlyList<MapSeatData> Seats,
    IReadOnlyList<SearchChoiceData> Issues, RecordListFilter Filter, string Measure, DateOnly? SelectedEnd)
{
    public IReadOnlyList<DateOnly> EndDates { get; init; } = [];
}
