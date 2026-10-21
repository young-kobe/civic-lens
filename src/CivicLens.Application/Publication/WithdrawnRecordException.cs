namespace CivicLens.Application.Publication;

public sealed class WithdrawnRecordException : ArgumentException
{
    public WithdrawnRecordException(PublishedRecordReference record)
        : base($"{record} is withdrawn and cannot be published again.") => UnpublishableRecord.Tag(this, record);
}
