using CivicLens.Application.Actions;

namespace CivicLens.Application.Publication;

/// <summary>The latest status and latest completed action history selected in one database snapshot.</summary>
public sealed record BillPublicationInput(string RecordId, ActionBillStatus? Status, BillHistoryCapture? History, string? PublishedHistoryDetailJobId);
