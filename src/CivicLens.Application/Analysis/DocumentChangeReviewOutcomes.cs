using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Application.Analysis;

public sealed record DocumentChangeReviewOutcomes(string PromptVersion, int Drafts, int ApprovedUnchanged, int ApprovedEdited,
    int ChangesRequested, int ApprovalWithdrawn, int AwaitingReview, ImmutableDictionary<DocumentChangeField, int> FieldEdits);
