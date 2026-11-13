using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.PublicWeb.Publication;

/// <summary>Builds the party bar of the issue line-up. It follows <see cref="VotePageView.CastBar"/>: the party rule is <see cref="PartyTones"/>, and each
/// segment has its own fill, so color is never the only cue. Democrats are solid, Republicans are striped, any other party is dotted, and the unknown count is open.</summary>
public static class IssueLineUpBar
{
    public const string UnknownLabel = "Sponsor party not known";

    public static PartyBarData Of(string issueName, IssueLineUpData lineUp)
    {
        List<PartyBarSegment> segments =
        [
            .. lineUp.Parties.OrderBy(party => PartyTones.Rank(party.Party)).ThenBy(party => party.Party, StringComparer.Ordinal)
                .Select(party => new PartyBarSegment($"{party.Party} sponsor", party.Bills, PartyTones.Of(party.Party), FillOf(party.Party)))
        ];
        if (lineUp.UnknownBills > 0) segments.Add(new(UnknownLabel, lineUp.UnknownBills, PartyTone.Quiet, PartyBarFill.Open));
        return new(IssueRecordWords.BillsHeading(issueName), segments);
    }

    private static PartyBarFill FillOf(string party) => PartyTones.Rank(party) switch
    {
        0 => PartyBarFill.Solid,
        1 => PartyBarFill.Stripes,
        _ => PartyBarFill.Dots
    };
}
