using CivicLens.Application.Actions;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// The latest status and latest completed action history selected in one database snapshot, with the status and history that the previous release pins.
/// </summary>
public sealed record BillPublicationInput(string RecordId, ActionBillStatus? Status, BillHistoryCapture? History,
    string? PublishedHistoryDetailJobId, int? PublishedHistoryProjectionVersion, PublishedBillStatus? PublishedStatus)
{
    public bool HasHistoryChange => History?.DetailJobId != PublishedHistoryDetailJobId ||
        History?.ProjectionVersion != PublishedHistoryProjectionVersion;
}
