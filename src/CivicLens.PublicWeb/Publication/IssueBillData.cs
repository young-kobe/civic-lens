namespace CivicLens.PublicWeb.Publication;

public sealed record IssueBillData(string RecordId, string Headline, string? Label, DateOnly EventDate, string? BillLine, string? BillLineSource);
