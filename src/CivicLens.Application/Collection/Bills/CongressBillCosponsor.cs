namespace CivicLens.Application.Collection.Bills;

public sealed record CongressBillCosponsor(
    string BioguideId,
    DateOnly SponsorshipDate,
    bool IsOriginalCosponsor,
    DateOnly? SponsorshipWithdrawnDate);
