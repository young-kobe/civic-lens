using CivicLens.Core.Review;
using CivicLens.Host.Components.Ui;

namespace CivicLens.Host.Review;

internal static class DraftStatusDisplay
{
    public static string Label(DocumentChangeReviewStatus status, int openConcerns) => status switch
    {
        DocumentChangeReviewStatus.AwaitingReviewWithConcerns => openConcerns == 1 ? "1 open concern" : $"{openConcerns} open concerns",
        DocumentChangeReviewStatus.Approved => "Approved, private",
        DocumentChangeReviewStatus.ChangesRequested => "Changes requested",
        DocumentChangeReviewStatus.ApprovalWithdrawn => "Approval withdrawn",
        _ => "Awaiting review"
    };

    public static Tone Tone(DocumentChangeReviewStatus status) => status switch
    {
        DocumentChangeReviewStatus.Approved => Components.Ui.Tone.Ok,
        DocumentChangeReviewStatus.AwaitingReview => Components.Ui.Tone.Neutral,
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
