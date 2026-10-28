namespace CivicLens.Application.Collection.MemberPhotos;

public static class MemberPhotoJobKeys
{
    private const string Prefix = "member-photo/";
    private const int PeriodDays = 30;
    private static readonly int UnixEpochDayNumber = DateOnly.FromDateTime(DateTime.UnixEpoch).DayNumber;

    public static string Photo(string sourceId, string bioguideId, DateOnly today) =>
        $"{Prefix}{sourceId}/{bioguideId}/{(today.DayNumber - UnixEpochDayNumber) / PeriodDays}";
}
