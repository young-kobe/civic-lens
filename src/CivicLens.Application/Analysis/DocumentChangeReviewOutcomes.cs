using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Application.Analysis;

/// <summary>How reviewers treated the AI drafts of one prompt version. Awaiting review includes drafts with carried concerns.</summary>
public sealed record DocumentChangeReviewOutcomes(string PromptVersion, int Drafts, int ApprovedUnchanged, int ApprovedEdited,
    int ChangesRequested, int ApprovalWithdrawn, int AwaitingReview, ImmutableDictionary<DocumentChangeField, int> FieldEdits);
