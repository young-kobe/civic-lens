namespace CivicLens.Application.People;

public sealed record PersonSettingRevision(string PersonId, int RevisionNumber, bool WithholdPhoto, string? Website);
