using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// How often the two parties agreed in one chamber and one Congress. The total counts the vote entries of that chamber and Congress that hold an alignment: agreed, split, and mixed.
/// It reads only the entry alignment, so <see cref="PublishedVote"/> stays the one owner of party agreement. The count exists only when every vote entry of the release names its Congress,
/// because an entry with no Congress cannot be placed in or out of the Congress of the board.
/// </summary>
public sealed record ChamberAgreement(PublishedChamber Chamber, int Congress, ImmutableArray<KindAgreement> Kinds)
{
    public int Agreed => Kinds.Sum(kind => kind.Agreed);

    public int Split => Kinds.Sum(kind => kind.Split);

    public int Mixed => Kinds.Sum(kind => kind.Mixed);

    public int Total => Agreed + Split + Mixed;

    public int Nominations => Kinds.Where(kind => kind.Kind == PublishedVoteKind.Nomination).Sum(kind => kind.Total);

    public static ChamberAgreement? From(PublicationRelease release, PublishedChamber chamber, int? congress)
    {
        var votes = release.Records.Where(entry => entry.Kind == PublishedRecordKind.Vote).ToList();
        if (congress is not { } shown || votes.Any(entry => entry.Congress is null)) return null;
        var kinds = votes.Where(entry => entry.Chamber == chamber && entry.Congress == shown && entry.Alignment is not null)
            .GroupBy(entry => entry.VoteKind!.Value).OrderBy(group => group.Key)
            .Select(group => new KindAgreement(group.Key, Count(group, PublishedVoteAlignment.PartiesAgree), Count(group, PublishedVoteAlignment.Split),
                Count(group, PublishedVoteAlignment.Mixed)));
        return new(chamber, shown, [.. kinds]);
    }

    private static int Count(IEnumerable<PublishedRecordEntry> entries, PublishedVoteAlignment alignment) => entries.Count(entry => entry.Alignment == alignment);
}
