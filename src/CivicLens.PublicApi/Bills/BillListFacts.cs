using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Bills;

/// <summary>The facts of one bill that the list shows beside its row. <c>Sponsor</c> is the name of the first sponsor in stored order, or null.
/// The latest action is the text and day that Congress.gov last listed, or null when Civic Lens has not read them. <c>Status</c> is the stored status of that text,
/// which <see cref="BillStatusMeaning.StageOf"/> wrote, or null when the release has not stored it.</summary>
public sealed record BillListFacts(string? Sponsor, string? LatestActionText, DateOnly? LatestActionDate, BillStatusStage? Status);
