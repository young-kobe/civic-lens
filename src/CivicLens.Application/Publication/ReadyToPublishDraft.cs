namespace CivicLens.Application.Publication;

public sealed record ReadyToPublishDraft(string DraftId, string Headline, int RevisionNumber, int? PublishedRevisionNumber)
{
    public bool IsReplacement => PublishedRevisionNumber is not null;
}
