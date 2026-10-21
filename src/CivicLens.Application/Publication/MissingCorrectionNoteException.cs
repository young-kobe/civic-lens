namespace CivicLens.Application.Publication;

public sealed class MissingCorrectionNoteException(PublishedRecordReference record)
    : ArgumentException($"{record} replaces a published record, so it needs a correction note.");
