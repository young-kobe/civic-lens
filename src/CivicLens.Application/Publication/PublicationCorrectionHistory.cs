using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed record PublicationCorrectionHistory(PublishedRevisionBinding Record, PublishedCorrection[] Corrections);
