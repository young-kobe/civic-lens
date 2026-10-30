using CivicLens.Application.Review;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Statements;

public static class StatementCodeApproval
{
    public const int DateWindowDays = 7;

    public static bool Approves(StatementDraftEvidence evidence, StatementAllowedSelections allowed, string? officeOf,
        StatementDraftRevision revision)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(allowed);
        ArgumentNullException.ThrowIfNull(revision);
        return evidence.CarryOver.GatePassed &&
            revision.IssueIds.All(issueId => evidence.CarryOver.IssueIds.Contains(issueId)) &&
            IsOnlySpokenBy(officeOf, revision) &&
            ApprovalCheck.Of(() => allowed.RequireApprovable(revision, evidence.Article.Extraction,
                IsDateReadByCode(revision.StatementDate, evidence.Article.FirstCapturedOn))).Passed;
    }

    private static bool IsOnlySpokenBy(string? officeOwner, StatementDraftRevision revision) =>
        officeOwner is not null && revision.OtherSpeakers.IsEmpty &&
        revision.SpeakerOfficialIds is [var speaker] && speaker == officeOwner;

    private static bool IsDateReadByCode(DateOnly? statementDate, DateOnly capturedOn) =>
        statementDate is { } date && date <= capturedOn && date >= capturedOn.AddDays(-DateWindowDays);
}
