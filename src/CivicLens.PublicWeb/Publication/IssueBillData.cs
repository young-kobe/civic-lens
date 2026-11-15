using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

public sealed record IssueBillData(string RecordId, string Headline, string? Label, PublishedTextOrigin TextOrigin, string BillType, string? BillLine, string? Sponsor,
    string? LatestActionText, DateOnly? LatestActionDate);
