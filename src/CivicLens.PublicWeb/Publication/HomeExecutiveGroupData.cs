using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

public sealed record HomeExecutiveGroupData(PublishedExecutiveActionKind Kind, int Count, string NewestRecordId, string NewestTitle, DateOnly NewestSignedOn);
