namespace CivicLens.Application.Publication;

public sealed class MissingCorrectionNoteException : ArgumentException
{
    public MissingCorrectionNoteException(PublishedRecordReference record)
        : base($"{record} replaces a published record, so it needs a correction note.") => UnpublishableRecord.Tag(this, record);
}
