namespace CivicLens.Publication.Contracts;

/// <summary>The coarse stage of a bill, which the public bills list filters on. <see cref="BillStatusMeaning"/> owns the rule that picks it.</summary>
public enum BillStatusStage
{
    InCommittee,
    PassedHouse,
    PassedSenate,
    BecameLaw,
    Other
}
