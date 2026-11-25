using CivicLens.Application.Collection;
using CivicLens.Application.Issues;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Statements;

public sealed class RecheckStatementCodeApprovals(IStatementCodeApprovalRecheckStore store)
{
    public async Task<StatementCodeApprovalRecheckResult> ExecuteAsync(ReviewActor actor, CollectionConfigurationRevision configuration,
        IssueConfiguration issues, int limit, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(issues);
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit), "The limit must be at least 1.");

        var selections = new StatementSelectionResolver(issues);
        var awaiting = await store.ListAwaitingAsync(limit, cancellationToken);
        var approved = 0;
        var stillHeld = 0;
        var skipped = 0;
        foreach (var candidate in awaiting)
        {
            var resolved = selections.Resolve(configuration, candidate.Evidence.Article.Extraction.SourceAttempt.SourceId,
                candidate.Evidence.Article.FirstCapturedOn);
            var check = StatementCodeApproval.Check(candidate.Evidence, resolved.Allowed, resolved.OfficeOf, candidate.CurrentRevision);
            switch (await store.ApplyAsync(candidate.CandidateId, candidate.CurrentRevision.RevisionNumber, check, cancellationToken))
            {
                case StatementCodeApprovalOutcome.Approved:
                    approved++;
                    break;
                case StatementCodeApprovalOutcome.StillHeld:
                    stillHeld++;
                    break;
                default:
                    skipped++;
                    break;
            }
        }
        return new(awaiting.Count, approved, stillHeld, skipped);
    }
}

public sealed record StatementCodeApprovalRecheckResult(int Checked, int Approved, int StillHeld, int Skipped);
