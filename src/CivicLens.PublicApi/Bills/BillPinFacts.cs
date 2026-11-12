using CivicLens.PublicWeb.Publication;

namespace CivicLens.PublicApi.Bills;

/// <summary>The facts that a bill number match shows. <c>Sponsor</c> is the name of the first sponsor in stored order.
/// <c>LawDate</c> is the day of the latest action when that action makes the bill law. <c>Vote</c> is the newest House roll call on the bill, or the newest Senate roll call when the bill has no House roll call.</summary>
public sealed record BillPinFacts(string RecordId, string? Sponsor, DateOnly? LawDate, SearchBillVoteData? Vote);
