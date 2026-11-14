using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One end of a relationship: the record, the revision that the approved link cites, and the current headline of the record with its text origin and label.
/// The label is the label column of the public view, and it is null for person text.</summary>
public sealed record RelationshipEndData(PublishedRecordKind Kind, string RecordId, int RevisionNumber, string Headline, PublishedTextOrigin TextOrigin, string? Label);
