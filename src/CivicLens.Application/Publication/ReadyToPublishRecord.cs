using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed record ReadyToPublishRecord(PublishedRecordKind Kind, string RecordId, string Headline, int RevisionNumber,
    int? PublishedRevisionNumber, bool NoLongerMatches = false, int? WithdrawnRevisionNumber = null)
{
    public PublishedRecordReference Reference => new(Kind, RecordId);
    public bool IsReplacement => PublishedRevisionNumber is not null;
    public bool IsWithdrawn => WithdrawnRevisionNumber is not null;
    public bool CanReturn => WithdrawnRevisionNumber is { } withdrawn && PublishedRecordRules.CanReturn(RevisionNumber, withdrawn);
    public bool IsSelectable => !IsWithdrawn || CanReturn;
    public bool NeedsNote => IsReplacement || IsWithdrawn;
}
