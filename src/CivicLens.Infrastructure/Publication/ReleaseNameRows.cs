using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Publication;

/// <summary>Adds the names of one release to the context. The rows share the release number, so a later release never changes them.</summary>
internal static class ReleaseNameRows
{
    public static void Add(CollectionAttemptDbContext db, int releaseNumber, ReleaseNames names)
    {
        foreach (var official in names.Officials)
        {
            db.Add(new ReleaseOfficialRow
            {
                ReleaseNumber = releaseNumber,
                OfficialId = official.Id,
                Name = official.Name,
                WikidataId = official.WikidataId,
                Website = official.Website,
                PhotoPath = official.Photo?.Path
            });
            db.AddRange(official.Terms.Select((term, position) => TermRow(releaseNumber, official.Id, (short)position, term)));
        }
        foreach (var issue in names.Facets.Issues) AddIssue(db, releaseNumber, issue);
        db.AddRange(names.Facets.PolicyAreas.Select(area =>
            new ReleasePolicyAreaRow { ReleaseNumber = releaseNumber, PolicyAreaId = area.Id, Name = area.Name }));
    }

    /// <summary>Fills the photo path of rows that hold none. A written path never changes, because the release is immutable.</summary>
    public static Task<int> FillPhotoPathsAsync(CollectionAttemptDbContext db, int releaseNumber, IReadOnlyDictionary<string, string> photoPaths,
        CancellationToken cancellationToken) => photoPaths.Count == 0 ? Task.FromResult(0) : db.Database.ExecuteSqlRawAsync("""
            UPDATE release_officials o SET photo_path = k.photo_path
            FROM unnest({1}::text[], {2}::text[]) AS k(official_id, photo_path)
            WHERE o.release_number = {0} AND o.official_id = k.official_id AND o.photo_path IS NULL
            """, [releaseNumber, photoPaths.Keys.ToArray(), photoPaths.Values.ToArray()], cancellationToken);

    private static void AddIssue(CollectionAttemptDbContext db, int releaseNumber, ReleaseFacet issue)
    {
        db.Add(new ReleaseIssueRow { ReleaseNumber = releaseNumber, IssueId = issue.Id, Name = issue.Name, Definition = issue.Definition?.Text });
        if (issue.Definition is null) return;
        db.AddRange(issue.Definition.Includes.Select((text, position) =>
            new ReleaseIssueIncludeRow { ReleaseNumber = releaseNumber, IssueId = issue.Id, Position = (short)position, Text = text }));
        db.AddRange(issue.Definition.Excludes.Select((text, position) =>
            new ReleaseIssueExcludeRow { ReleaseNumber = releaseNumber, IssueId = issue.Id, Position = (short)position, Text = text }));
    }

    private static ReleaseOfficialTermRow TermRow(int releaseNumber, string officialId, short position, PublishedOfficeTerm term) => new()
    {
        ReleaseNumber = releaseNumber,
        OfficialId = officialId,
        Position = position,
        Office = term.Office,
        Kind = term.Kind,
        State = term.State,
        District = term.District,
        Party = term.Party,
        Caucus = term.Caucus,
        StartOn = term.Start,
        EndOn = term.End
    };
}
