namespace CivicLens.PublicWeb.Publication;

/// <summary>The query pair of a link to the next or the previous page of a keyset list, and the words of the two links.
/// The next page reads the rows after its cursor row. The previous page reads the rows before its cursor row, the first row of this page (#580 item 7).
/// The one owner of the two parameter names. The PublicApi list requests read them.</summary>
public sealed record ListPageLink(string Name, string Cursor)
{
    public const string NextName = "cursor";
    public const string PreviousName = "before";
    public const string NextText = "Next page";
    public const string PreviousText = "Previous page";

    public static ListPageLink Next(string cursor) => new(NextName, cursor);

    public static ListPageLink Previous(string cursor) => new(PreviousName, cursor);

    public string QueryPair => $"{Name}={Uri.EscapeDataString(Cursor)}";
}
