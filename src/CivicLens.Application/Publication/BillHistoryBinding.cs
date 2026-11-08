namespace CivicLens.Application.Publication;

/// <summary>The immutable history copy that a release pins for one bill record.</summary>
public sealed record BillHistoryBinding(string RecordId, string DetailJobId, int ProjectionVersion);
