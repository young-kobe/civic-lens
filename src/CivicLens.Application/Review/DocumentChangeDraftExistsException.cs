namespace CivicLens.Application.Review;

public sealed class DocumentChangeDraftExistsException(string draftId)
    : InvalidOperationException("This change already has a draft.")
{
    public string DraftId { get; } = draftId;
}
