using CivicLens.PublicWeb.Publication;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

public sealed record PublishedKeyFigure(string Value, string Label, string Note)
{
    public string HelpLabel => $"What this means: {Label}";

    public static string SectionLabel(int congress) => $"Key figures for the {CongressCalendar.Ordinal(congress)} Congress";

    public static IReadOnlyList<PublishedKeyFigure> Of(TalkAndActionProfile profile, int congress) =>
    [
        .. profile.MissedByChamber.Select(missed => Voted(missed, profile.MissedByChamber.Length > 1)),
        .. profile.WithPartyByChamber.Where(votes => votes.Cast > 0).Select(WithParty),
        Sponsored(profile.SponsoredBills.Length, congress),
        Statements(profile.StatementsInCongress, congress)
    ];

    private static PublishedKeyFigure Voted(MissedVotes missed, bool namesChamber)
    {
        var voted = missed.Held - missed.Missed;
        var noun = PublishedOfficialView.Noun(missed.Held, "roll call");
        var rollCalls = namesChamber ? $"{missed.Chamber} {noun} voted on" : $"{noun} voted on";
        return missed.Share is null
            ? new($"{voted} of {missed.Held}", rollCalls, VoteWords.MissedVoteMeaning)
            : new(PublishedOfficialView.Percent(voted, missed.Held), $"of {rollCalls}: {voted} of {missed.Held}", VoteWords.MissedVoteMeaning);
    }

    private static PublishedKeyFigure WithParty(VotesWithParty votes)
    {
        var party = PublishedOfficialView.PartyOf(votes);
        var splitVotes = $"split {PublishedOfficialView.Noun(votes.Cast, "vote")}";
        var note = votes.NotCast == 0
            ? VoteWords.SplitMeaning
            : $"{VoteWords.SplitMeaning} They did not cast Yes or No on {votes.NotCast} other split {PublishedOfficialView.Noun(votes.NotCast, "vote")}.";
        return TalkAndActionProfile.ShareOf(votes.Matched, votes.Cast) is null
            ? new($"{votes.Matched} of {votes.Cast}", $"{splitVotes} with most {party}", note)
            : new(PublishedOfficialView.Percent(votes.Matched, votes.Cast), $"with most {party}: {votes.Matched} of {votes.Cast} {splitVotes}", note);
    }

    private static PublishedKeyFigure Sponsored(int count, int congress) => new($"{count}",
        count == 1 ? "bill or resolution sponsored" : "bills and resolutions sponsored", $"Counts the bills of this official in this release, in the {CongressCalendar.Ordinal(congress)} Congress.");

    private static PublishedKeyFigure Statements(int count, int congress) => new($"{count}", $"{PublishedOfficialView.Noun(count, "statement")} published so far",
        count == 0
            ? "This means we have not yet published a statement from this official. It does not mean they made none."
            : $"Counts the statements of this official in this release, in the {CongressCalendar.Ordinal(congress)} Congress.");
}
