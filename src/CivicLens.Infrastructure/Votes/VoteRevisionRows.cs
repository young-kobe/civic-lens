using System.Collections.Immutable;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Votes.Persistence;

namespace CivicLens.Infrastructure.Votes;

/// <summary>Owns the mapping between a vote revision and its row, fact rows, and issue rows, in both directions. The row also keeps the JSON until readers use the columns.</summary>
internal static class VoteRevisionRows
{
    public static VoteRevisionRow Create(VoteDraftRevision revision, string? runId)
    {
        var row = new VoteRevisionRow
        {
            RecordId = revision.RecordId,
            RevisionNumber = revision.RevisionNumber,
            AuthorSubject = revision.AuthorSubject,
            AuthorKind = ReviewAuthor.KindOf(revision.AuthorSubject),
            RunId = runId,
            RevisionJson = ReviewJson.Serialize(revision),
            CreatedAtUtcTicks = revision.CreatedAtUtc.UtcTicks
        };
        FillColumns(row, revision);
        return row;
    }

    /// <summary>Sets every column and child row of the revision. It leaves the key, the author, the run, and the JSON as they are.</summary>
    public static void FillColumns(VoteRevisionRow row, VoteDraftRevision revision)
    {
        row.Headline = revision.Headline;
        row.Summary = revision.Summary;
        row.TextOrigin = revision.TextOrigin;
        row.SuggestionHeadline = revision.Suggestion?.Headline;
        row.SuggestionSummary = revision.Suggestion?.Summary;
        row.SuggestionRunId = revision.Suggestion?.RunId;
        row.SuggestionFingerprint = revision.Suggestion?.Fingerprint;
        row.SourceId = revision.SourceId;
        row.VoteSource = revision.Source;
        row.Chamber = revision.Chamber;
        row.Congress = revision.Congress;
        row.SessionNumber = revision.SessionNumber;
        row.RollCallNumber = revision.RollCallNumber;
        row.BillType = revision.BillType;
        row.BillNumber = revision.BillNumber;
        row.CandidateId = revision.CandidateId;
        row.CandidateUpdateUtcTicks = revision.CandidateUpdateDate.UtcTicks;
        row.VotedOn = revision.VotedOn;
        row.VotedOnCitationExtractionId = revision.VotedOnCitation.ExtractionId;
        row.VotedOnCitationStart = revision.VotedOnCitation.Start;
        row.VotedOnCitationLength = revision.VotedOnCitation.Length;
        row.Kind = revision.Kind;
        row.KindCitationExtractionId = revision.KindCitation?.ExtractionId;
        row.KindCitationStart = revision.KindCitation?.Start;
        row.KindCitationLength = revision.KindCitation?.Length;
        row.MemberListId = VoteMemberLists.IdOf(revision.Members);
        row.IssueRevisionId = revision.IssueRevisionId;
        row.SubjectsHash = revision.SubjectsHash;
        row.IssueCandidatesRecorded = !revision.IssueCandidateIds.IsDefault;
        row.Facts = [.. revision.Facts.Select((fact, position) => FactRow(revision, fact, position))];
        row.Issues = [.. IssueIds(revision).Select((issueId, position) => IssueRow(revision, issueId, position))];
    }

    /// <summary>Builds the revision back from a row with columns. The tracked issues come back in ordinal order, because the drafting code stores them that way.</summary>
    public static VoteDraftRevision Rebuild(VoteRevisionRow row, ImmutableArray<MemberVote> members)
    {
        var issues = row.Issues.OrderBy(issue => issue.Position).ToArray();
        var recorded = row.IssueCandidatesRecorded!.Value;
        var tracked = issues.Where(issue => issue.IsTracked).Select(issue => issue.IssueId);
        return new(row.RecordId, row.RevisionNumber, row.AuthorSubject, new DateTimeOffset(row.CreatedAtUtcTicks, TimeSpan.Zero),
            row.Headline!, row.Summary!, row.TextOrigin!.Value, SuggestionOf(row),
            row.SourceId!, row.VoteSource!.Value, row.Chamber!.Value, row.Congress!.Value, row.SessionNumber!.Value, row.RollCallNumber!.Value,
            row.BillType, row.BillNumber, row.CandidateId!, new DateTimeOffset(row.CandidateUpdateUtcTicks!.Value, TimeSpan.Zero),
            [.. row.Facts.OrderBy(fact => fact.Position).Select(fact => new VoteFact(fact.FactText, new(fact.CitationExtractionId, fact.CitationStart, fact.CitationLength)))],
            row.VotedOn!.Value, new(row.VotedOnCitationExtractionId!, row.VotedOnCitationStart!.Value, row.VotedOnCitationLength!.Value), row.Kind!.Value,
            row.KindCitationExtractionId is null ? null : new(row.KindCitationExtractionId, row.KindCitationStart!.Value, row.KindCitationLength!.Value), members,
            [.. recorded ? tracked.Order(StringComparer.Ordinal) : tracked],
            recorded ? [.. issues.Select(issue => issue.IssueId)] : default, row.IssueRevisionId!, row.SubjectsHash);
    }

    private static ReviewTextSuggestion? SuggestionOf(VoteRevisionRow row) => row.SuggestionHeadline is null
        ? null
        : new(row.SuggestionHeadline, row.SuggestionSummary!, row.SuggestionRunId!, row.SuggestionFingerprint!);

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
