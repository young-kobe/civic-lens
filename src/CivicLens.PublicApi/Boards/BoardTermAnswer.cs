using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Boards;

public sealed record BoardTermAnswer(string Office, PublishedOfficeKind Kind, string? State, int? District,
    string? Party, string? Caucus, DateOnly StartsOn, DateOnly? EndsOn, string? HouseOffice);
