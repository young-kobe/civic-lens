namespace CivicLens.Application.Collection.HouseVotes;

public static class HouseVoteRollUrl
{
    private const string Query = "?format=json";
    private const string MembersSegment = "/members";

    public static string Detail(string sourceUrl, HouseVoteRoll roll) => $"{RollPath(sourceUrl, roll)}{Query}";

    public static string Members(string sourceUrl, HouseVoteRoll roll) => $"{RollPath(sourceUrl, roll)}{MembersSegment}{Query}";

    public static HouseVoteRoll? TryReadDetail(string sourceUrl, string url) => TryRead(sourceUrl, url, Query);

    public static HouseVoteRoll? TryReadMembers(string sourceUrl, string url) => TryRead(sourceUrl, url, MembersSegment + Query);

    public static HouseVoteRoll ReadDetail(string sourceUrl, string url) =>
        TryReadDetail(sourceUrl, url) ?? throw new InvalidDataException("Job URL is not a House vote detail URL.");

    public static HouseVoteRoll ReadMembers(string sourceUrl, string url) =>
        TryReadMembers(sourceUrl, url) ?? throw new InvalidDataException("Job URL is not a House vote members URL.");

    private static string RollPath(string sourceUrl, HouseVoteRoll roll) =>
        $"{sourceUrl}/{roll.Session.Congress}/{roll.Session.Number}/{roll.Number}";

    private static HouseVoteRoll? TryRead(string sourceUrl, string url, string suffix)
    {
        var prefix = sourceUrl + "/";
        if (!url.StartsWith(prefix, StringComparison.Ordinal) || !url.EndsWith(suffix, StringComparison.Ordinal))
            return null;
        var parts = url[prefix.Length..^suffix.Length].Split('/');
        if (parts.Length != 3 || HouseVoteSession.TryRead(parts[0], parts[1]) is not { } session) return null;
        return ApiPageQuery.TryReadNumber(parts[2]) is >= 1 and var number ? new HouseVoteRoll(session, number) : null;
    }
}
