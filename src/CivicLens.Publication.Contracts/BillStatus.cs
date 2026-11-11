namespace CivicLens.Publication.Contracts;

/// <summary>The known statuses that <see cref="BillStatusMeaning"/> reads from a latest action text. A text that no rule matches is <see cref="Unmatched"/>.</summary>
public enum BillStatus
{
    InCommittee,
    PassedHouse,
    PassedSenate,
    OnSenateCalendar,
    OnHouseCalendar,
    Reported,
    BecameLaw,
    Unmatched
}
