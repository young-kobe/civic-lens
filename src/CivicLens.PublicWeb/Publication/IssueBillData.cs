using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

public sealed record IssueBillData(string RecordId, string Headline, string? Label, PublishedTextOrigin TextOrigin, DateOnly EventDate, string? BillLine, string? Sponsor);
