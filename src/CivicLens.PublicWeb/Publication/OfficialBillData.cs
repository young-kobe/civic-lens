namespace CivicLens.PublicWeb.Publication;

/// <summary>One bill the official sponsored. <c>IntroducedOn</c> is the introduced date of the Congress.gov bill record. <c>LatestAction</c> is the latest official action text, when the release pins one.</summary>
public sealed record OfficialBillData(string RecordId, string BillType, string? BillLine, string BillTitle, DateOnly IntroducedOn, string? LatestAction, DateOnly? LatestActionOn);
