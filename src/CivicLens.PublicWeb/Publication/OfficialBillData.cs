namespace CivicLens.PublicWeb.Publication;

/// <summary>One bill the official sponsored. <c>IntroducedOn</c> is the sponsorship date of the official. <c>LatestAction</c> is the latest official action text, when the release pins one.</summary>
public sealed record OfficialBillData(string RecordId, string BillType, string? BillLine, string BillTitle, DateOnly IntroducedOn, string? LatestAction, DateOnly? LatestActionOn);
