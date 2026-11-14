using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Boards;

/// <summary>Maps the four-board answer to the presentation record of the server page. The answer comes from one repeatable-read snapshot, so the lists and the agreement share a release.</summary>
public sealed class BoardsPageReader(BoardStore boards)
{
    /// <summary>The most members that one panel shows. The full board page holds the full list.</summary>
    public const int PanelSize = 10;

    public async Task<BoardsPageData?> ReadAsync(CancellationToken cancellationToken)
    {
        if (await boards.FindAsync(cancellationToken) is not { } answer) return null;
        var panels = answer.Boards.Select(board => Panel(board, PanelSize)).ToArray();
        var agreements = answer.Agreements.Where(item => item.Agreement is not null).Select(item => Agreement(item.Agreement!)).ToArray();
        return new(answer.Release, panels, agreements);
    }

    /// <summary>The full list of one board. It reads the same four-board snapshot as the boards page, so both pages rank members by one rule.</summary>
    public async Task<BoardListPageData?> ReadBoardAsync(OfficialBoard board, CancellationToken cancellationToken)
    {
        if (await boards.FindAsync(cancellationToken) is not { } answer) return null;
        var page = answer.Boards.Single(item => item.Board.Chamber == board.Chamber && item.Board.Party == board.Party);
        return new(answer.Release, Panel(page, int.MaxValue));
    }

    /// <summary>Delegates and the resident commissioner rank in their own list. They cast far fewer House roll call votes, so a share of their small base never ranks against voting members.
    /// <see cref="BoardPanelData.Ranked"/> counts voting members only, so a board whose only ranked members are delegates states that no voting member is ranked.</summary>
    private static BoardPanelData Panel(BoardPageAnswer board, int size)
    {
        var members = (board.Members ?? []).ToDictionary(member => member.OfficialId, StringComparer.Ordinal);
        var ranked = (board.Breakers ?? []).Select(id => members[id]).ToLookup(IsDelegate);
        var rows = ranked[false].Where(member => member.Breaks > 0).Take(size).Select(member => Row(member, null)).ToArray();
        var delegates = ranked[true].Where(member => member.Breaks > 0).Select(member => Row(member, member.Terms[^1])).ToArray();
        var newest = members.Values.Select(member => member.MissedVotes?.LastVotedOn).Max();
        return new(board.Board.Chamber, board.Board.Party, board.Congress, board.NewerCongress, newest, ranked[false].Any(), rows) { Delegates = delegates };
    }

    /// <summary>A member is a delegate or the resident commissioner when their newest term of the board holds a House office other than Representative.</summary>
    private static bool IsDelegate(BoardMemberAnswer member) => member.Terms.LastOrDefault()?.HouseOffice is not null;

    private static BoardRowData Row(BoardMemberAnswer member, BoardTermAnswer? delegateTerm) => new(member.PagePath, member.Name,
        delegateTerm is null ? null : OfficialSeat.Text(delegateTerm.Kind, delegateTerm.Office, delegateTerm.State, delegateTerm.District), member.Cast, member.Breaks);

    private static BoardAgreementData Agreement(PartyAgreement.ChamberAgreementAnswer agreement) => new(agreement.Chamber, agreement.Congress,
        [.. agreement.Kinds.Select(kind => new BoardAgreementKindData(kind.Kind, kind.Agreed, kind.Total))],
        agreement.Agreed, agreement.Split, agreement.Mixed, agreement.Total, agreement.Nominations);
}
