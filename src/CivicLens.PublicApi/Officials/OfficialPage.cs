namespace CivicLens.PublicApi.Officials;

/// <summary>A page of officials. <c>RecordCountScope</c> says what each <c>recordCount</c> counts.</summary>
public sealed record OfficialPage(int Release, string RecordCountScope, OfficialRow[] Officials, string? NextCursor);
