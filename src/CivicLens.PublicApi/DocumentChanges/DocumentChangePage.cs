namespace CivicLens.PublicApi.DocumentChanges;

public sealed record DocumentChangePage(int Release, DocumentChangeListRow[] Changes, string? NextCursor);
