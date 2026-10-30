namespace CivicLens.Application.Publication;

public sealed class WithdrawnRecordException : ArgumentException
{
    public WithdrawnRecordException(PublishedRecordReference record)
        : base($"{record} is withdrawn, so only a person can bring it back.") => UnpublishableRecord.Tag(this, record);
}
