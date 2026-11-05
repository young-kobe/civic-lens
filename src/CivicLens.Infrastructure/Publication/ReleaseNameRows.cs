using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication.Persistence;
using CivicLens.Publication.Contracts;

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
                Website = official.Website
            });
            db.AddRange(official.Terms.Select((term, position) => TermRow(releaseNumber, official.Id, (short)position, term)));
        }
        foreach (var issue in names.Facets.Issues) AddIssue(db, releaseNumber, issue);
        db.AddRange(names.Facets.PolicyAreas.Select(area =>
            new ReleasePolicyAreaRow { ReleaseNumber = releaseNumber, PolicyAreaId = area.Id, Name = area.Name }));
    }

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
