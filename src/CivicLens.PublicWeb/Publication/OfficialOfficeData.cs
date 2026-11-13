namespace CivicLens.PublicWeb.Publication;

/// <summary>One dated term of an official, as the release names it. <c>Seat</c> is the words of <see cref="OfficialSeat"/>. A null <c>EndsOn</c> means that the release states no end.</summary>
public sealed record OfficialOfficeData(string Seat, string? Party, DateOnly StartsOn, DateOnly? EndsOn);
