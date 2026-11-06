using System.Linq.Expressions;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Review.Persistence;

namespace CivicLens.Infrastructure.Review;

/// <summary>The one rule for a person final decision: Approve, Request changes, or Reject, by an author that is not code.
/// A withdrawal and a code approval are not final decisions of a person.</summary>
internal static class PersonFinalDecisions
{
    private static readonly ReviewDecisionKind[] Kinds = [ReviewDecisionKind.Approve, ReviewDecisionKind.RequestChanges, ReviewDecisionKind.Reject];

    public static Expression<Func<TRow, bool>> Match<TRow>() where TRow : IReviewDecisionRow =>
        decision => Kinds.Contains(decision.Kind) && decision.ActorSubject != ReviewAuthor.CodeSubject;
}
