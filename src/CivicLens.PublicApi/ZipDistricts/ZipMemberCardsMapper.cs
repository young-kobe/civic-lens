using CivicLens.PublicApi.Issues;
using CivicLens.PublicWeb.Publication;
using CivicLens.PublicApi.Officials;
using CivicLens.PublicApi.Votes;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.ZipDistricts;

/// <summary>Maps one ZIP snapshot to presentation inputs without reading any data again.</summary>
internal static class ZipMemberCardsMapper
{
    public static ZipMemberCardsData Map(ZipDistrictAnswer answer, SharedMemberVoteAnswer? sharedVotes,
        IssueOption[] issueOptions, string? selectedIssue)
    {
        if (sharedVotes is not null && sharedVotes.Release != answer.Release)
            throw new InvalidDataException("ZIP members and shared votes must come from the same release.");

        var seats = new List<ZipMemberSeatData>();
        var withoutSenators = new List<string>();
        foreach (var district in answer.Districts)
            seats.Add(new(HouseTitle(district), Member(district.Representative)));
        foreach (var state in answer.States)
        {
            var stateName = UsStates.NameOf(state.State);
            if (state.Senators.Length == 0) withoutSenators.Add(stateName);
            foreach (var senator in state.Senators)
                seats.Add(new($"Senator for {stateName}", Member(senator)) { Chamber = PublishedChamber.Senate });
        }
        SharedVoteRowsData[] sharedRows = sharedVotes is null ? [] : SharedRows(answer, seats, sharedVotes);
        return new ZipMemberCardsData(answer.Release, answer.Congress, answer.Zcta, answer.Districts.Length, [.. seats], [.. withoutSenators])
        {
            SharedVotes = sharedRows,
            IssueOptions = [.. issueOptions.Select(option => new ZipMemberIssueFilterData(option.IssueId, option.Name))],
            SelectedIssue = selectedIssue
        };
    }

    private static string HouseTitle(ZipDistrict district)
    {
        var state = UsStates.NameOf(district.State);
        if (UsStates.HouseOfficeByNonStateCode.TryGetValue(district.State, out var office)) return $"{office} for {state}";
        return district.District == 0 ? $"Representative for {state}, at large" : $"Representative for {state}, district {district.District}";
    }

    private static ZipMemberIdentityData? Member(SeatHolder? holder) => holder is null ? null
        : new ZipMemberIdentityData(holder.OfficialId, holder.Name, holder.Party, holder.TermEndsOn, holder.PhotoPath,
            [.. holder.MissedVotes.Select(figure => new ZipMemberAttendanceData(figure.Chamber, figure.Congress, figure.Missed, figure.Held,
                figure.FirstVotedOn, figure.LastVotedOn, figure.Scope))],
            [.. holder.PartyLines.Select(figure => new ZipMemberPartyLineData(figure.Chamber, figure.Congress,
                figure.Party, figure.WithParty, figure.Cast, figure.FirstVotedOn, figure.LastVotedOn, figure.Scope))])
        {
            Bills = holder.Bills is { } bills ? new(bills.Congress, bills.Sponsored, bills.Scope) : null,
            BillCountsAbsent = OfficialActivityFigures.BillCountsAbsent(holder.Bills, holder.Issues),
            Issues = holder.Issues is { } issues ? [.. issues.Issues.Select(issue => new ZipMemberIssueData(
                string.IsNullOrWhiteSpace(issue.Name) ? issue.IssueId : issue.Name,
                issues.Congress, issue.SponsoredBills, issue.VotesCast, issues.Scope,
                !string.IsNullOrWhiteSpace(issue.Name) && PublicationProtocol.IsPathSegment(issue.IssueId)
                    ? PublicationProtocol.IssuePagePath(issue.IssueId, 1)
                    : null))] : []
        };

    private static SharedVoteRowsData[] SharedRows(ZipDistrictAnswer answer, List<ZipMemberSeatData> seats, SharedMemberVoteAnswer sharedVotes)
    {
        if (sharedVotes.OfficialIds.Distinct(StringComparer.Ordinal).Count() != sharedVotes.OfficialIds.Length)
            throw new InvalidDataException("Shared votes must list each ZIP member once.");

        var houseIds = answer.Districts.Where(district => district.Representative is not null)
            .Select(district => district.Representative!.OfficialId).Distinct(StringComparer.Ordinal).ToArray();
        var senateIds = answer.States.SelectMany(state => state.Senators).Where(senator => senator is not null)
            .Select(senator => senator!.OfficialId).Distinct(StringComparer.Ordinal).ToArray();
        var expectedIds = houseIds.Concat(senateIds).ToHashSet(StringComparer.Ordinal);
        if (!expectedIds.SetEquals(sharedVotes.OfficialIds))
            throw new InvalidDataException("Shared votes must include every current ZIP member.");
        foreach (var vote in sharedVotes.Votes)
            ValidateVoteCasts(vote, expectedIds);

        var membersById = seats.Select(seat => seat.Member).OfType<ZipMemberIdentityData>()
            .GroupBy(member => member.OfficialId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        return new[]
            {
                (Chamber: PublishedChamber.House, Ids: houseIds),
                (Chamber: PublishedChamber.Senate, Ids: senateIds)
            }
            .Select(group => (group.Chamber, Members: group.Ids.Select(id => membersById[id]).ToArray()))
            .Where(group => group.Members.Length > 0)
            .Select(group => new SharedVoteRowsData(group.Chamber,
                [.. group.Members.Select(member => new SharedVoteMemberData(member.OfficialId, member.Name))],
                [.. sharedVotes.Votes.Where(vote => vote.Chamber == group.Chamber).Select(vote => SharedRow(vote, group.Members))]))
            .Where(group => group.Votes.Length > 0)
            .ToArray();
    }

    private static SharedVoteRowData SharedRow(SharedMemberVote vote, ZipMemberIdentityData[] members)
    {
        var memberIds = members.Select(member => member.OfficialId).ToHashSet(StringComparer.Ordinal);
        var casts = vote.Casts.Where(cast => memberIds.Contains(cast.OfficialId))
            .Select(cast => new SharedVoteCastData(cast.OfficialId, cast.CastWords, PublishedOfficialVote.SideOf(cast.Cast))).ToArray();
        var sides = casts.Select(cast => cast.Side).ToArray();
        bool? membersVotedTheSame = null;
        if (members.Length >= 2 && casts.Length == members.Length && sides.All(side => side is not null))
            membersVotedTheSame = sides.Distinct(StringComparer.Ordinal).Count() == 1;
        return new(vote.RecordId, vote.VotedOn, vote.Congress, vote.Session, vote.RollCall, vote.Headline, vote.Label,
            vote.TextOrigin, vote.KindWords, vote.ResultWords, casts)
        {
            MembersVotedTheSame = membersVotedTheSame
        };
    }

    private static void ValidateVoteCasts(SharedMemberVote vote, HashSet<string> zipMemberIds)
    {
        if (vote.Casts.Select(cast => cast.OfficialId).Distinct(StringComparer.Ordinal).Count() != vote.Casts.Length ||
            vote.Casts.Any(cast => !zipMemberIds.Contains(cast.OfficialId)))
            throw new InvalidDataException("A shared vote must contain unique casts from current ZIP members.");
    }
}
