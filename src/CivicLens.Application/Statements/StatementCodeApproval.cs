using CivicLens.Application.Review;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Statements;

public static class StatementCodeApproval
{
    public static bool Approves(StatementDraftEvidence evidence, StatementAllowedSelections allowed, StatementDraftRevision revision)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(allowed);
        ArgumentNullException.ThrowIfNull(revision);
        return evidence.CarryOver.GatePassed &&
            revision.IssueIds.All(issueId => evidence.CarryOver.IssueIds.Contains(issueId)) &&
            IsOnlySpokenBy(OwnerOfSource(evidence), revision) &&
            ApprovalCheck.Of(() => allowed.RequireApprovable(revision, evidence.Article.Extraction)).Passed;
    }

    private static string? OwnerOfSource(StatementDraftEvidence evidence)
    {
        var sourceId = evidence.Article.Extraction.SourceAttempt.SourceId;
        return evidence.ConfigurationRevision?.Configuration.Sources.SingleOrDefault(source => source.Id == sourceId)?.OfficeOf;
    }

    private static bool IsOnlySpokenBy(string? officeOwner, StatementDraftRevision revision) =>
        officeOwner is not null && revision.OtherSpeakers.IsEmpty &&
        revision.SpeakerOfficialIds is [var speaker] && speaker == officeOwner;
}
