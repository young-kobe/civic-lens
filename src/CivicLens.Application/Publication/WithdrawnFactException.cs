namespace CivicLens.Application.Publication;

public sealed class WithdrawnFactException : ArgumentException
{
    public WithdrawnFactException(PublishedRecordReference record)
        : base($"{record} is withdrawn, so only a person can bring it back.") => UnpublishableRecord.Tag(this, record);
}
