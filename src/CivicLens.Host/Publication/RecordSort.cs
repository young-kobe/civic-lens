namespace CivicLens.Host.Publication;

/// <summary>Where a paged record table asks for its sorted rows, and how many records the whole list holds. The filter is a query string such as <c>issue=housing</c>.</summary>
public sealed record RecordSort(string ApiUrl, int Total, string? Filter = null);
