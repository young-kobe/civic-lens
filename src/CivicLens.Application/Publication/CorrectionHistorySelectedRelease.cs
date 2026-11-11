namespace CivicLens.Application.Publication;

/// <summary>A currently selected release and the cursor that starts a walk at its first record.</summary>
public sealed record CorrectionHistorySelectedRelease(int ReleaseNumber, string DirectoryName, string Cursor);
