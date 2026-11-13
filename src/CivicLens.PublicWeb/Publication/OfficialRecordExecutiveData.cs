using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One executive action in the list of an official.</summary>
public sealed record OfficialRecordExecutiveData(string RecordId, string Title, PublishedExecutiveActionKind Kind, DateOnly SignedOn);
