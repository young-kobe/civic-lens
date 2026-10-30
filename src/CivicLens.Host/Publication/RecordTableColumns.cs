namespace CivicLens.Host.Publication;

public enum RecordTableColumns
{
    FirstPublished,
    Facet,
    Week
}

public static class RecordTableColumnNames
{
    /// <summary>The names that public.js uses to build a row of a sorted table. Week tables never sort.</summary>
    public static string[] ForSort(this RecordTableColumns columns) => columns switch
    {
        RecordTableColumns.Facet => ["record", "officials", "date"],
        RecordTableColumns.FirstPublished => ["record", "published"],
        _ => []
    };
}
