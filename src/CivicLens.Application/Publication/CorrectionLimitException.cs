using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed class CorrectionLimitException(PublishedRecordReference record)
    : ArgumentException($"{record} already has {PublishedCorrection.MaximumNotedEntries} corrections with a note.");
