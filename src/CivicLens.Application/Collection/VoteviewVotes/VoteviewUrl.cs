using System.Text.RegularExpressions;

namespace CivicLens.Application.Collection.VoteviewVotes;

public static partial class VoteviewUrl
{
    private static readonly Dictionary<ApiResource, string> FileNames = new()
    {
        [ApiResource.VoteviewRollCalls] = "rollcalls",
        [ApiResource.VoteviewVotes] = "votes",
        [ApiResource.VoteviewMembers] = "members"
    };

    public static string File(string sourceUrl, ApiResource resource, int congress) =>
        $"{sourceUrl}/{FileNames[resource]}/S{congress}_{FileNames[resource]}.csv";

    public static int? TryReadCongress(string sourceUrl, ApiResource resource, string url)
    {
        if (!FileNames.ContainsKey(resource) || !url.StartsWith(sourceUrl + "/", StringComparison.Ordinal) ||
            FilePattern().Match(url[(sourceUrl.Length + 1)..]) is not { Success: true } match)
            return null;
        var congress = ApiPageQuery.TryReadNumber(match.Groups[1].Value);
        return congress is >= 1 && File(sourceUrl, resource, congress.Value) == url ? congress : null;
    }

    public static int ReadCongress(string sourceUrl, ApiResource resource, string url) =>
        TryReadCongress(sourceUrl, resource, url) ?? throw new InvalidDataException("Job URL is not a Voteview file URL.");

    [GeneratedRegex(@"^(?:rollcalls|votes|members)/S(\d{1,9})_(?:rollcalls|votes|members)\.csv$")]
    private static partial Regex FilePattern();
}
