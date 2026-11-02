using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Votes.Persistence;

namespace CivicLens.Infrastructure.Votes;

/// <summary>Owns the mapping from a vote revision to its row, fact rows, and issue rows. The row also keeps the JSON until readers use the columns.</summary>
internal static class VoteRevisionRows
{
    public static VoteRevisionRow Create(VoteDraftRevision revision, string? runId) => new()
    {
        RecordId = revision.RecordId,
        RevisionNumber = revision.RevisionNumber,
        AuthorSubject = revision.AuthorSubject,
        AuthorKind = ReviewAuthor.KindOf(revision.AuthorSubject),
        RunId = runId,
        RevisionJson = ReviewJson.Serialize(revision),
        CreatedAtUtcTicks = revision.CreatedAtUtc.UtcTicks,
        Headline = revision.Headline,
        Summary = revision.Summary,
        TextOrigin = revision.TextOrigin,
        SuggestionHeadline = revision.Suggestion?.Headline,
        SuggestionSummary = revision.Suggestion?.Summary,
        SuggestionRunId = revision.Suggestion?.RunId,
        SuggestionFingerprint = revision.Suggestion?.Fingerprint,
        SourceId = revision.SourceId,
        VoteSource = revision.Source,
        Chamber = revision.Chamber,
        Congress = revision.Congress,
        SessionNumber = revision.SessionNumber,
        RollCallNumber = revision.RollCallNumber,
        BillType = revision.BillType,
        BillNumber = revision.BillNumber,
        CandidateId = revision.CandidateId,
        CandidateUpdateUtcTicks = revision.CandidateUpdateDate.UtcTicks,
        VotedOn = revision.VotedOn,
        VotedOnCitationExtractionId = revision.VotedOnCitation.ExtractionId,
        VotedOnCitationStart = revision.VotedOnCitation.Start,
        VotedOnCitationLength = revision.VotedOnCitation.Length,
        Kind = revision.Kind,
        KindCitationExtractionId = revision.KindCitation?.ExtractionId,
        KindCitationStart = revision.KindCitation?.Start,
        KindCitationLength = revision.KindCitation?.Length,
        MemberListId = VoteMemberLists.IdOf(revision.Members),
        IssueRevisionId = revision.IssueRevisionId,
        SubjectsHash = revision.SubjectsHash,
        IssueCandidatesRecorded = !revision.IssueCandidateIds.IsDefault,
        Facts = [.. revision.Facts.Select((fact, position) => FactRow(revision, fact, position))],
        Issues = [.. IssueIds(revision).Select((issueId, position) => IssueRow(revision, issueId, position))]
    };

    private static VoteRevisionFactRow FactRow(VoteDraftRevision revision, VoteFact fact, int position) => new()
    {
        RecordId = revision.RecordId,
        RevisionNumber = revision.RevisionNumber,
        Position = (short)position,
        FactText = fact.Text,
        CitationExtractionId = fact.Citation.ExtractionId,
        CitationStart = fact.Citation.Start,
        CitationLength = fact.Citation.Length
    };

    private static IEnumerable<string> IssueIds(VoteDraftRevision revision) =>
        revision.IssueCandidateIds.IsDefault ? revision.TrackedIssueIds : revision.IssueCandidateIds;

    private static VoteRevisionIssueRow IssueRow(VoteDraftRevision revision, string issueId, int position) => new()
    {
        RecordId = revision.RecordId,
        RevisionNumber = revision.RevisionNumber,
        IssueId = issueId,
        Position = (short)position,
        IsTracked = revision.TrackedIssueIds.Contains(issueId)
    };
}
