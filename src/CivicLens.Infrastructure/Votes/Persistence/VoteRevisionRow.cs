using CivicLens.Publication.Contracts;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Review.Persistence;

namespace CivicLens.Infrastructure.Votes.Persistence;

internal sealed class VoteRevisionRow : IReviewRevisionRow, IReviewTextRow
{
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string AuthorSubject { get; set; } = string.Empty;
    public ReviewAuthorKind AuthorKind { get; set; }
    public string? RunId { get; set; }
    public long CreatedAtUtcTicks { get; set; }
    public string? Headline { get; set; }
    public string? Summary { get; set; }
    public ReviewTextOrigin? TextOrigin { get; set; }
    public string? SuggestionHeadline { get; set; }
    public string? SuggestionSummary { get; set; }
    public string? SuggestionRunId { get; set; }
    public string? SuggestionFingerprint { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public VoteSource VoteSource { get; set; }
    public Chamber Chamber { get; set; }
    public int Congress { get; set; }
    public int SessionNumber { get; set; }
    public int RollCallNumber { get; set; }
    public string? BillType { get; set; }
    public int? BillNumber { get; set; }
    public string CandidateId { get; set; } = string.Empty;
    public long CandidateUpdateUtcTicks { get; set; }
    public DateOnly VotedOn { get; set; }
    public string VotedOnCitationExtractionId { get; set; } = string.Empty;
    public int VotedOnCitationStart { get; set; }
    public int VotedOnCitationLength { get; set; }
    public VoteKind Kind { get; set; }
    public string? KindCitationExtractionId { get; set; }
    public int? KindCitationStart { get; set; }
    public int? KindCitationLength { get; set; }
    public string MemberListId { get; set; } = string.Empty;
    public string IssueRevisionId { get; set; } = string.Empty;
    public string? SubjectsHash { get; set; }
    public bool IssueCandidatesRecorded { get; set; }
    public string? Question { get; set; }
    public string Result { get; set; } = string.Empty;
    public bool ColumnsWritten { get; set; }
    public short? PartyTallyCount { get; set; }
    public PublishedVoteAlignment? Alignment { get; set; }
    public string? DemocratMajority { get; set; }
    public string? RepublicanMajority { get; set; }
    public List<VoteRevisionFactRow> Facts { get; set; } = [];
    public List<VoteRevisionIssueRow> Issues { get; set; } = [];
    public List<VoteRevisionPartyTallyRow> PartyTallies { get; set; } = [];
}
