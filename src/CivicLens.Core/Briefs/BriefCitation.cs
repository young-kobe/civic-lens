namespace CivicLens.Core.Briefs;

public sealed record BriefCitation(string Id, BriefCitationKind Kind, string RecordId, int RevisionNumber);
