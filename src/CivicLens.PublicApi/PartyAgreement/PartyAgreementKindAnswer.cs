using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.PartyAgreement;

public sealed record PartyAgreementKindAnswer(PublishedVoteKind Kind, int Agreed, int Split, int Mixed, int Total);
