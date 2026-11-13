namespace CivicLens.PublicWeb.Publication;

/// <summary>The bill text version that an AI text summary cites, as <c>public_api.bills</c> serves it. The counts are the characters of that version that the model read, and all of them.</summary>
public sealed record BillTextSourceData(string VersionName, DateOnly? VersionDate, int CharsRead, int CharsTotal);
