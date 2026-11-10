using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed record StoredCorrectionHistory(CorrectionHistoryBinding Binding, PublishedCorrection[] Corrections);
