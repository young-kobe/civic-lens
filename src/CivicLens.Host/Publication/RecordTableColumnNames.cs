namespace CivicLens.Host.Publication;

public static class RecordTableColumnNames
{
    /// <summary>The names that public.js uses to build a row of a sorted table. Week tables never sort.</summary>
    public static string[] ForSort(this RecordTableColumns columns) => columns switch
    {
        RecordTableColumns.Facet => ["record", "officials", "date"],
        RecordTableColumns.Dated => ["record", "date"],
        _ => throw new ArgumentOutOfRangeException(nameof(columns), columns, "Record table columns do not sort.")
    };
}
