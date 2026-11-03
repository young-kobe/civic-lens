using CivicLens.Application;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Actions.Persistence;
using CivicLens.Infrastructure.Review;

namespace CivicLens.Infrastructure.Actions;

internal static class ActionRevisionRows
{
    public static ActionRevisionRow Create(ActionDraftRevision revision, string? runId, CodeVersion codeVersion)
    {
        var authorKind = ReviewAuthor.KindOf(revision.AuthorSubject);
        var row = new ActionRevisionRow
        {
            RecordId = revision.RecordId,
            RevisionNumber = revision.RevisionNumber,
            AuthorSubject = revision.AuthorSubject,
            AuthorKind = authorKind,
            RunId = runId,
            CodeVersion = authorKind == ReviewAuthorKind.Code ? codeVersion.Sha : null,
            RevisionJson = ReviewJson.Serialize(revision),
            CreatedAtUtcTicks = revision.CreatedAtUtc.UtcTicks
        };
        FillColumns(row, revision);
        return row;
    }

    private static void FillColumns(ActionRevisionRow row, ActionDraftRevision revision)
    {
        ReviewTextRows.Fill(row, revision);
        row.SourceId = revision.SourceId;
        row.Congress = revision.Congress;
        row.BillType = revision.BillType;
        row.BillNumber = revision.BillNumber;
        row.CandidateId = revision.CandidateId;
        row.CandidateUpdateUtcTicks = revision.CandidateUpdateDate.UtcTicks;
        row.CandidateIncludesSummaries = revision.CandidateIncludesSummaries;
        row.CandidateIncludesSubjects = revision.CandidateIncludesSubjects;
        row.Fingerprint = revision.Fingerprint;
        row.SummaryVersion = revision.SummaryVersion;
        row.SummaryCitationExtractionId = revision.SummaryCitation?.ExtractionId;
        row.SummaryCitationStart = revision.SummaryCitation?.Start;
        row.SummaryCitationLength = revision.SummaryCitation?.Length;
        row.PolicyArea = revision.PolicyArea;
        row.PolicyAreaCitationExtractionId = revision.PolicyAreaCitation?.ExtractionId;
        row.PolicyAreaCitationStart = revision.PolicyAreaCitation?.Start;
        row.PolicyAreaCitationLength = revision.PolicyAreaCitation?.Length;
        row.PolicyAreaId = revision.PolicyAreaId;
        row.BillTitle = revision.BillTitle;
        row.BillCitationExtractionId = revision.BillCitation?.ExtractionId;
        row.BillCitationStart = revision.BillCitation?.Start;
        row.BillCitationLength = revision.BillCitation?.Length;
        row.IssueRevisionId = revision.IssueRevisionId;
        row.SubjectsHash = revision.SubjectsHash;
        row.IssueCandidatesRecorded = !revision.IssueCandidateIds.IsDefault;
        row.Issues = [.. IssueIds(revision).Select((issueId, position) => IssueRow(revision, issueId, position))];
        row.Sponsorships = [.. revision.Sponsorships.Select((sponsorship, position) => SponsorshipRow(revision, sponsorship, position))];
    }

    private static IEnumerable<string> IssueIds(ActionDraftRevision revision) =>
        revision.IssueCandidateIds.IsDefault ? revision.TrackedIssueIds : revision.IssueCandidateIds;

    private static ActionRevisionIssueRow IssueRow(ActionDraftRevision revision, string issueId, int position) => new()
    {
        RecordId = revision.RecordId,
        RevisionNumber = revision.RevisionNumber,
        IssueId = issueId,
        Position = (short)position,
        IsTracked = revision.TrackedIssueIds.Contains(issueId)
    };

    private static ActionRevisionSponsorshipRow SponsorshipRow(ActionDraftRevision revision, ActionSponsorship sponsorship, int position) => new()
    {
        RecordId = revision.RecordId,
        RevisionNumber = revision.RevisionNumber,
        Position = (short)position,
        PersonId = sponsorship.PersonId,
        BioguideId = sponsorship.BioguideId,
        Role = sponsorship.Role,
        IsOriginal = sponsorship.IsOriginal,
        SponsoredOn = sponsorship.SponsoredOn,
        WithdrawnOn = sponsorship.WithdrawnOn,
        Citations = [.. sponsorship.Citations.Select((citation, citationPosition) => CitationRow(revision, (short)position, citation, citationPosition))]
    };

    private static ActionRevisionSponsorshipCitationRow CitationRow(ActionDraftRevision revision, short sponsorshipPosition, SourceSpan citation, int position) => new()
    {
        RecordId = revision.RecordId,
        RevisionNumber = revision.RevisionNumber,
        SponsorshipPosition = sponsorshipPosition,
        Position = (short)position,
        CitationExtractionId = citation.ExtractionId,
        CitationStart = citation.Start,
        CitationLength = citation.Length
    };
}
