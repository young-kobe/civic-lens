namespace CivicLens.Application.Publication;

public sealed class WithdrawnRecordException : ArgumentException
{
    public WithdrawnRecordException(PublishedRecordReference record)
        : base($"{record} is withdrawn. It returns only when a person approves a higher revision with a correction note.") => UnpublishableRecord.Tag(this, record);
}
