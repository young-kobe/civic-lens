namespace CivicLens.Application.Publication;

/// <summary>Names the record whose validation failed while a release loads it. The exception keeps its own type.</summary>
internal static class UnpublishableRecord
{
    private const string DataKey = "civic-lens:record";

    public static void Tag(ArgumentException exception, PublishedRecordReference record) => exception.Data[DataKey] = record;

    public static PublishedRecordReference? Of(Exception exception) => exception.Data[DataKey] as PublishedRecordReference;
}
