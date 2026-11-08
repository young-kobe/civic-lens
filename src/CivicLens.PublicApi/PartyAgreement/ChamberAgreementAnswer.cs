using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.PartyAgreement;

public sealed record ChamberAgreementAnswer(
    PublishedChamber Chamber,
    int Congress,
    IReadOnlyList<PartyAgreementKindAnswer> Kinds,
    int Agreed,
    int Split,
    int Mixed,
    int Total,
    int Nominations);
