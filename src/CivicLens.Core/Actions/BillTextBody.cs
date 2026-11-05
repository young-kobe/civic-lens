namespace CivicLens.Core.Actions;

/// <summary>Where the bill text starts in a rendered text extraction. The header lines come before the text label.</summary>
public static class BillTextBody
{
    public const string Label = "Text:";

    /// <summary>The offset where the bill text starts, or -1 when the header has no text label.</summary>
    public static int Start(string renderedText)
    {
        ArgumentNullException.ThrowIfNull(renderedText);
        var label = $"\n{Label}\n";
        var index = renderedText.IndexOf(label, StringComparison.Ordinal);
        return index < 0 ? -1 : index + label.Length;
    }
}
