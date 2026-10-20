using System.Collections.Immutable;
using System.Text;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Writes the vote files, the page of each official, and the page that lists the officials.</summary>
internal sealed class ReleaseOfficialPages(ReleaseOfficialVotes votes, IReleaseRenderer renderer)
{
    public async Task WriteAsync(IReleaseStaging staging, PublicationRelease manifest, PublishedOfficials officials,
        IReadOnlyList<PendingRecord> additions, PreviousRelease previous, CancellationToken cancellationToken)
    {
        var votesByOfficial = await votes.WriteAsync(staging, officials, additions, previous, cancellationToken);
        var entriesByOfficial = manifest.Records.SelectMany(entry => entry.Officials.Select(official => (official.Id, Entry: entry)))
            .ToLookup(pair => pair.Id, pair => pair.Entry, StringComparer.Ordinal);
        var pages = officials.Officials.Where(official => PublicationProtocol.IsPathSegment(official.Id))
            .Select(official => new ReleaseOfficial(official, [.. entriesByOfficial[official.Id]],
                votesByOfficial.TryGetValue(official.Id, out var rows) ? rows : []))
            .ToList();
        foreach (var page in pages)
            await WriteAsync(staging, page.PagePath, await renderer.RenderOfficialAsync(page, cancellationToken), cancellationToken);
        await WriteAsync(staging, PublicationProtocol.OfficialsPagePath, await renderer.RenderOfficialsAsync(pages, cancellationToken),
            cancellationToken);
    }

    private static Task WriteAsync(IReleaseStaging staging, string path, string html, CancellationToken cancellationToken) =>
        staging.WriteFileAsync(path, Encoding.UTF8.GetBytes(html), cancellationToken);
}
