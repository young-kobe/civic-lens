using CivicLens.Application.Review;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Statements;

public static class StatementCodeApproval
{
    public const int DateWindowDays = 7;

    public const string GateNotPassed = "The issue gate did not pass this article.";
    public const string IssueNotInGate = "The draft names an issue that the issue gate did not find in the article.";
    public const string SpeakerIsNotOfficeOwner = "The office owner of the source is not the only speaker of the draft.";

    public static ApprovalCheck Check(StatementDraftEvidence evidence, StatementAllowedSelections allowed, string? officeOf,
        StatementDraftRevision revision)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(allowed);
        ArgumentNullException.ThrowIfNull(revision);
        if (!evidence.CarryOver.GatePassed) return new(GateNotPassed);
        if (!revision.IssueIds.All(issueId => evidence.CarryOver.IssueIds.Contains(issueId))) return new(IssueNotInGate);
        if (!IsOnlySpokenBy(officeOf, revision)) return new(SpeakerIsNotOfficeOwner);
        return ApprovalCheck.Of(() => allowed.RequireApprovable(revision, evidence.Article.Extraction,
            IsDateReadByCode(revision.StatementDate, evidence.Article.FirstCapturedOn)));
    }

    private static bool IsOnlySpokenBy(string? officeOwner, StatementDraftRevision revision) =>
        officeOwner is not null && revision.OtherSpeakers.IsEmpty &&
        revision.SpeakerOfficialIds is [var speaker] && speaker == officeOwner;

    private static bool IsDateReadByCode(DateOnly? statementDate, DateOnly capturedOn) =>
        statementDate is { } date && date <= capturedOn && date >= capturedOn.AddDays(-DateWindowDays);
}
