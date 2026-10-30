using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed class CorrectionLimitException : ArgumentException
{
    public CorrectionLimitException(PublishedRecordReference record)
        : base($"{record} already has {PublishedCorrection.MaximumNotedEntries} corrections with a note.") =>
        UnpublishableRecord.Tag(this, record);
}
