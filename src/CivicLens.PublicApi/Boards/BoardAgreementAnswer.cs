using CivicLens.PublicApi.PartyAgreement;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Boards;

public sealed record BoardAgreementAnswer(PublishedChamber Chamber, ChamberAgreementAnswer? Agreement);
