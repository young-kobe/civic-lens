namespace CivicLens.Application.Publication;

/// <summary>A release that the cursor named but the current selection no longer holds under that number and directory.</summary>
public sealed record CorrectionHistorySkippedRelease(int ReleaseNumber, string DirectoryName, string Reason)
{
    public const string NotSelected = "notSelected";
    public const string DirectoryChanged = "directoryChanged";
}
