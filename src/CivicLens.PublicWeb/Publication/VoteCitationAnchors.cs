using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Evidence;

namespace CivicLens.PublicWeb.Publication;

/// <summary>Gives each citation of a vote page one anchor, so a fact line or a member row reaches its marked line in the source text.</summary>
public sealed class VoteCitationAnchors
{
    private readonly Dictionary<PublishedCitation, string> anchors = [];

    public VoteCitationAnchors(VotePageData vote)
    {
        IEnumerable<PublishedCitation> citations = [vote.VotedOnCitation, .. vote.KindCitation is { } kind ? [kind] : Array.Empty<PublishedCitation>(),
            .. vote.Facts.Select(fact => fact.Citation), .. vote.Members.Select(member => member.Citation)];
        var index = 0;
        foreach (var citation in citations)
        {
            anchors.TryAdd(citation, $"cite-{index + 1}");
            index++;
        }
    }

    public string Anchor(PublishedCitation citation) => anchors[citation];

    public IReadOnlyList<CitedRange> RangesIn(PublishedDocumentVersion source) =>
        [.. anchors.Where(item => item.Key.ExtractionId == source.ExtractionId).Select(item => new CitedRange(item.Value, item.Key.Start, item.Key.Length))];
}
