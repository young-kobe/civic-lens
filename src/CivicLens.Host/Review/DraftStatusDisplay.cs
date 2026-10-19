using CivicLens.Core.Review;
using CivicLens.Host.Components.Ui;

namespace CivicLens.Host.Review;

internal static class DraftStatusDisplay
{
    public static string Label(ReviewStatus status, int openConcerns) => status switch
    {
        ReviewStatus.AwaitingReviewWithConcerns => openConcerns == 1 ? "1 open concern" : $"{openConcerns} open concerns",
        ReviewStatus.Approved => "Approved, private",
        ReviewStatus.ChangesRequested => "Changes requested",
        ReviewStatus.ApprovalWithdrawn => "Approval withdrawn",
        ReviewStatus.NotDrafted => "Not drafted",
        _ => "Awaiting review"
    };

    public static string LabelWithoutCount(ReviewStatus status) =>
        status == ReviewStatus.AwaitingReviewWithConcerns ? "Open concerns" : Label(status, 0);

    public static Tone Tone(ReviewStatus status) => status switch
    {
        ReviewStatus.Approved => Components.Ui.Tone.Ok,
        ReviewStatus.AwaitingReview or ReviewStatus.NotDrafted => Components.Ui.Tone.Neutral,
        _ => Components.Ui.Tone.Warn
    };

    public static string Label(ReviewDecisionKind kind) => kind switch
    {
        ReviewDecisionKind.Approve => "Approved",
        ReviewDecisionKind.RequestChanges => "Changes requested",
        _ => "Approval withdrawn"
    };

    public static Tone Tone(ReviewDecisionKind kind) =>
        kind == ReviewDecisionKind.Approve ? Components.Ui.Tone.Ok : Components.Ui.Tone.Warn;

    public static string DraftName(string headline) =>
        string.IsNullOrWhiteSpace(headline) ? "Untitled draft" : headline;
}
