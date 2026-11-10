using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Records;

public sealed record CorrectionHistoryDetail(int Release, PublishedRecordKind Kind, string RecordId, int RevisionNumber, PublishedCorrection[] Corrections);
