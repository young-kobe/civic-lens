using CivicLens.Core.Review;

namespace CivicLens.Application.Analysis;

public sealed record AiDraftOutcome(string PromptVersion, DocumentChangeDraftRevision FirstRevision,
    DocumentChangeReviewListItem Current);
