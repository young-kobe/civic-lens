using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Ui;
using CivicLens.PublicWeb.Publication;

namespace CivicLens.PublicApi.Bills;

/// <summary>The facts of one bill that the list shows beside its row. <c>Sponsor</c> is the name of the first sponsor in stored order, or null.
/// The latest action is the text and day that Congress.gov last listed, or null when Civic Lens has not read them. <c>Status</c> is the stored status of that text,
/// which <see cref="BillStatusMeaning.StageOf"/> wrote, or null when the release has not stored it.</summary>
public sealed record BillListFacts(string? Sponsor, string? LatestActionText, DateOnly? LatestActionDate, BillStatusStage? Status,
    BillKeyTermData[] KeyTerms, string? KeyTermsLabel)
{
    internal BillTermContext? HeadlineTerms(string recordId, int revisionNumber, int congress, string billType, int billNumber,
        string? textVersionName, DateOnly? textVersionDate) =>
        KeyTerms.Length == 0 ? null
            : new(recordId, revisionNumber, KeyTerms, KeyTermsLabel, CongressGovBillPage.Url(congress, billType, billNumber),
                textVersionName is { } name ? BillTextWords.SourceLine(name, textVersionDate is { } date ? Moment.Day(date) : null) : null);
}
