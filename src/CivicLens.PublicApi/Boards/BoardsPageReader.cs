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

    private static BoardPanelData Panel(BoardPageAnswer board, int size)
    {
        var members = (board.Members ?? []).ToDictionary(member => member.OfficialId, StringComparer.Ordinal);
        var ranked = (board.Breakers ?? []).Select(id => members[id]).ToArray();
        var rows = ranked.Where(member => member.Breaks > 0).Take(size).Select(Row).ToArray();
        var newest = members.Values.Select(member => member.MissedVotes?.LastVotedOn).Max();
        return new(board.Board.Chamber, board.Board.Party, board.Congress, board.NewerCongress, newest, ranked.Length > 0, rows);
    }

    private static BoardRowData Row(BoardMemberAnswer member)
    {
        var term = member.Terms.LastOrDefault();
        var seat = term?.HouseOffice is { } office ? $"{office} for {term.State}" : null;
        return new(member.PagePath, member.Name, seat, member.Cast, member.Breaks);
    }

    private static BoardAgreementData Agreement(PartyAgreement.ChamberAgreementAnswer agreement) => new(agreement.Chamber, agreement.Congress,
        [.. agreement.Kinds.Select(kind => new BoardAgreementKindData(kind.Kind, kind.Agreed, kind.Total))],
        agreement.Agreed, agreement.Split, agreement.Mixed, agreement.Total, agreement.Nominations);
}
