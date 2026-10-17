using System.Collections.Immutable;
using CivicLens.Application.Issues;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Statements;

public sealed class SaveGateLabel(IGateLabelStore store, IssueConfiguration issues)
{
    private readonly ImmutableHashSet<string> trackedIssueIds = [.. issues.Issues.Select(issue => issue.Id)];

    public Task ExecuteAsync(ReviewActor actor, SaveGateLabelRequest request, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentNullException.ThrowIfNull(request);
        var label = new GateLabel(ReviewValidation.ValidateSelections(request.IssueIds, trackedIssueIds, "issueIds"), request.NamesBillOrVote);
        return store.AddAsync(new GateLabelEntry(ReviewValidation.ValidateCandidateId(request.CandidateId),
            ReviewValidation.ValidateHash(request.ExtractionId, "extractionId"), actor.Subject, ReviewAuthorKind.Person, null, label,
            DateTimeOffset.UtcNow), cancellationToken);
    }
}
