using System.Collections.Immutable;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;
using CivicLens.Infrastructure.Actions.Persistence;
using CivicLens.Infrastructure.Review;

namespace CivicLens.Infrastructure.Actions;

/// <summary>Action revision rows with their issue, sponsorship, and citation rows. A page costs a fixed number of queries.</summary>
internal sealed record ActionRevisionPage(IReadOnlyList<ActionRevisionRow> Rows,
    ILookup<(string RecordId, int RevisionNumber), ActionRevisionIssueRow> Issues,
    ILookup<(string RecordId, int RevisionNumber), ActionRevisionSponsorshipRow> Sponsorships,
    ILookup<(string RecordId, int RevisionNumber, short SponsorshipPosition), ActionRevisionSponsorshipCitationRow> Citations,
    ILookup<(string RecordId, int RevisionNumber), ActionRevisionTextCitationRow> TextCitations,
    ILookup<(string RecordId, int RevisionNumber), ActionRevisionKeyTermRow> KeyTerms) : IRevisionRowPage<ActionRevisionRow>
{
    /// <summary>Rebuilds the revision of a row. A row with null columns fails here, and the JSON is no fallback.</summary>
    public ActionDraftRevision Rebuild(ActionRevisionRow row)
    {
        if (row.Headline is null)
            throw ActionRevisionRows.NotBackfilled($"Action revision {row.RecordId} {row.RevisionNumber}");
        var issues = Issues[(row.RecordId, row.RevisionNumber)].OrderBy(issue => issue.Position).ToArray();
        var recorded = row.IssueCandidatesRecorded!.Value;
        var tracked = issues.Where(issue => issue.IsTracked).Select(issue => issue.IssueId);
        return new(row.RecordId, row.RevisionNumber, row.AuthorSubject, new DateTimeOffset(row.CreatedAtUtcTicks, TimeSpan.Zero),
            row.Headline, row.Summary!, row.TextOrigin!.Value, ReviewTextRows.SuggestionOf(row),
            row.SourceId!, row.Congress!.Value, row.BillType!, row.BillNumber!.Value, row.CandidateId!,
            new DateTimeOffset(row.CandidateUpdateUtcTicks!.Value, TimeSpan.Zero), row.CandidateIncludesSummaries!.Value, row.CandidateIncludesSubjects!.Value,
            row.Fingerprint!, row.SummaryVersion, SpanOf(row.SummaryCitationExtractionId, row.SummaryCitationStart, row.SummaryCitationLength),
            row.PolicyArea, SpanOf(row.PolicyAreaCitationExtractionId, row.PolicyAreaCitationStart, row.PolicyAreaCitationLength), row.PolicyAreaId,
            [.. tracked], recorded ? [.. issues.Select(issue => issue.IssueId)] : default,
            row.IssueRevisionId!, SponsorshipsOf(row), row.BillTitle!, row.SubjectsHash,
            SpanOf(row.BillCitationExtractionId, row.BillCitationStart, row.BillCitationLength), row.TextPackageId, row.WhyItMatters,
            TextCitationsOf(row), KeyTermsOf(row));
    }

    public IReadOnlyList<StoredRevision<ActionRevisionRow, ActionDraftRevision>> ToStoredRevisions() =>
        [.. Rows.Select(row => new StoredRevision<ActionRevisionRow, ActionDraftRevision>(row, () => Rebuild(row)))];

    /// <summary>A revision with no text citations keeps the default array, so its JSON leaves the field out.</summary>
    private ImmutableArray<ActionTextCitation> TextCitationsOf(ActionRevisionRow row) =>
        row.TextPackageId is null ? default
            : [.. TextCitations[(row.RecordId, row.RevisionNumber)].OrderBy(citation => citation.Position)
                .Select(citation => new ActionTextCitation(citation.Part, new SourceSpan(citation.CitationExtractionId, citation.CitationStart, citation.CitationLength), citation.Sentence))];

    /// <summary>A revision with no key terms keeps the default array, so its JSON leaves the field out.</summary>
    private ImmutableArray<ActionKeyTerm> KeyTermsOf(ActionRevisionRow row)
    {
        var terms = KeyTerms[(row.RecordId, row.RevisionNumber)].OrderBy(term => term.Position).Select(term =>
            new ActionKeyTerm(term.Term, new SourceSpan(term.CitationExtractionId, term.CitationStart, term.CitationLength), term.Definition, term.Cut)).ToImmutableArray();
        return terms.IsEmpty ? default : terms;
    }

    private ImmutableArray<ActionSponsorship> SponsorshipsOf(ActionRevisionRow row) =>
        [.. Sponsorships[(row.RecordId, row.RevisionNumber)].OrderBy(item => item.Position).Select(item => new ActionSponsorship(item.PersonId, item.BioguideId,
            item.Role, item.IsOriginal, item.SponsoredOn, item.WithdrawnOn,
            [.. Citations[(item.RecordId, item.RevisionNumber, item.Position)].OrderBy(citation => citation.Position)
                .Select(citation => new SourceSpan(citation.CitationExtractionId, citation.CitationStart, citation.CitationLength))]))];

    private static SourceSpan? SpanOf(string? extractionId, int? start, int? length) =>
        extractionId is null ? null : new(extractionId, start!.Value, length!.Value);
}
