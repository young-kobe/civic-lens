namespace CivicLens.Application.Collection;

public enum ApiParser
{
    CongressBillList,
    CongressHouseVoteList,
    /// <summary>Retired: stored configuration revisions still name it. A configuration cannot enable it.</summary>
    SenateVoteList,
    VoteviewSenateVoteList,
    CongressLegislators,
    CongressLegislatorsExecutive,
    MemberPhotos,
    FederalRegisterDocuments
}
