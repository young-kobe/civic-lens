namespace CivicLens.PublicWeb.Publication;

public sealed record ZipMemberCardsData(int Release, int Congress, string Zcta, int DistrictCount, ZipMemberSeatData[] Seats, string[] StatesWithoutSenators);
