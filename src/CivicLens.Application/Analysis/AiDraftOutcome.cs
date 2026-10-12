using CivicLens.Core.Review;

namespace CivicLens.Application.Analysis;

/// <summary>An AI draft with the prompt version that wrote it, its first revision, and its current review state.</summary>
public sealed record AiDraftOutcome(string PromptVersion, DocumentChangeDraftRevision FirstRevision,
    DocumentChangeReviewListItem Current);
