using CivicLens.Application.Reports;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Review;

internal static class ReportLabels
{
    private const int ExcerptCharacters = 120;

    public static string Category(string category) => category switch
    {
        ReaderReportProtocol.WrongQuoteCategory => "Wrong quote",
        ReaderReportProtocol.WrongDateCategory => "Wrong date",
        ReaderReportProtocol.WrongAttributionCategory => "Wrong attribution",
        ReaderReportProtocol.MissingContextCategory => "Missing context",
        ReaderReportProtocol.BrokenSourceLinkCategory => "Broken source link",
        ReaderReportProtocol.OtherCategory => "Other",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Report category has no label.")
    };

    public static string Status(ReaderReportStatus status) => status switch
    {
        ReaderReportStatus.Open => "Open",
        ReaderReportStatus.Dismissed => "Dismissed",
        ReaderReportStatus.CorrectionNeeded => "Correction needed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Report status has no label.")
    };

    public static string Revision(ReaderReportListItem item) =>
        item.CurrentRevisionNumber is { } current && current != item.Report.RevisionNumber
            ? $"Revision {item.Report.RevisionNumber}, current is {current}"
            : $"Revision {item.Report.RevisionNumber}";

    public static string Excerpt(string text) =>
        text.Length <= ExcerptCharacters ? text : text[..ExcerptCharacters] + "...";

    public static string? EditorOf(ReaderReportListItem item) =>
        item.CurrentRevisionNumber is null ? null : RecordRoutes.Editor(item.Report.Kind, item.Report.RecordId);
}
