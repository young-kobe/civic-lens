using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Actions;

/// <summary>The status row of an action record with the bill type, which the plain meaning of the status needs. The published row holds no bill type.</summary>
public sealed record ActionBillStatus(PublishedBillStatus Status, string BillType);
