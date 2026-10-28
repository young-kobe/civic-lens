namespace CivicLens.Application.Roster;

public sealed record OfficialPhoto(string PersonId, string SourceUrl, string Credit, string AttemptId, string CaptureSha256);
