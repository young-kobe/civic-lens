namespace CivicLens.Application.Roster;

public sealed record MemberPhoto(string AttemptId, string PersonId, string SourceUrl, string Credit)
{
    public const string MemberGuideCredit = "Official portrait from the GPO Member Guide, via unitedstates/images. Public domain.";
}
