namespace CivicLens.Application.Relationships;

public static class RelationshipDraftErrorCodes
{
    public const string EvidenceUnavailable = "evidenceUnavailable";
    public const string SideNotApproved = "sideNotApproved";
    public const string OverByteCap = "overByteCap";
    public const string Unchanged = "unchanged";
    public const string NoRelationship = "noRelationship";
    public const string NoLongerMatches = "noLongerMatches";
    public const string RevisionConflict = "revisionConflict";
}
