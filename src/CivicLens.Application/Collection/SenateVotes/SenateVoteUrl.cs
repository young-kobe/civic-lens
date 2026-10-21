using System.Globalization;
using System.Text.RegularExpressions;
using CivicLens.Application.Collection.Votes;

namespace CivicLens.Application.Collection.SenateVotes;

public static partial class SenateVoteUrl
{
    public static string Menu(string sourceUrl, VoteSession session) =>
        $"{sourceUrl}/roll_call_lists/vote_menu_{session.Congress}_{session.Number}.xml";

    public static string Vote(string sourceUrl, SenateVoteRoll roll) =>
        $"{sourceUrl}/roll_call_votes/vote{roll.Session.Congress}{roll.Session.Number}/" +
        $"vote_{roll.Session.Congress}_{roll.Session.Number}_{roll.Number.ToString("00000", CultureInfo.InvariantCulture)}.xml";

    public static VoteSession? TryReadMenu(string sourceUrl, string url)
    {
        if (!url.StartsWith(sourceUrl + "/", StringComparison.Ordinal) || MenuPattern().Match(url[(sourceUrl.Length + 1)..]) is not { Success: true } match)
            return null;
        var session = VoteSession.TryRead(match.Groups[1].Value, match.Groups[2].Value);
        return session is { } found && Menu(sourceUrl, found) == url ? found : null;
    }

    public static SenateVoteRoll? TryReadVote(string sourceUrl, string url)
    {
        if (!url.StartsWith(sourceUrl + "/", StringComparison.Ordinal) || VotePattern().Match(url[(sourceUrl.Length + 1)..]) is not { Success: true } match ||
            VoteSession.TryRead(match.Groups[1].Value, match.Groups[2].Value) is not { } session)
            return null;
        var roll = new SenateVoteRoll(session, int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
        return roll.Number >= 1 && Vote(sourceUrl, roll) == url ? roll : null;
    }

    public static VoteSession ReadMenu(string sourceUrl, string url) =>
        TryReadMenu(sourceUrl, url) ?? throw new InvalidDataException("Job URL is not a Senate vote menu URL.");

    public static SenateVoteRoll ReadVote(string sourceUrl, string url) =>
        TryReadVote(sourceUrl, url) ?? throw new InvalidDataException("Job URL is not a Senate vote URL.");

    [GeneratedRegex(@"^roll_call_lists/vote_menu_(\d{1,9})_(\d)\.xml$")]
    private static partial Regex MenuPattern();

    [GeneratedRegex(@"^roll_call_votes/vote\d{1,10}/vote_(\d{1,9})_(\d)_(\d{5})\.xml$")]
    private static partial Regex VotePattern();
}
