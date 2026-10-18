using CivicLens.Application.Publication;
using CivicLens.Core.Relationships;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Relationships;

public static class RelationshipRecordKinds
{
    public static PublishedRecordKind PublishedKindOf(RelationshipRecordKind kind) => kind switch
    {
        RelationshipRecordKind.Statement => PublishedRecordKind.Statement,
        RelationshipRecordKind.Action => PublishedRecordKind.Action,
        RelationshipRecordKind.Vote => PublishedRecordKind.Vote,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Relationship record kind has no published record kind.")
    };

    public static PublishedRevisionBinding BindingOf(RelationshipCitation citation) =>
        new(PublishedKindOf(citation.Kind), citation.RecordId, citation.RevisionNumber);
}
