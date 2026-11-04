namespace CivicLens.Host.Publication;

public sealed record OfficialSection(string Id, string Title, IReadOnlyList<PublishedOfficialRow> Rows, int Total, string? SeeAllText, string? SeeAllHref, string EmptyText);
