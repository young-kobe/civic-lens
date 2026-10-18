namespace CivicLens.Infrastructure.Review.Persistence;

internal interface IListedDraftRow : IReviewDraftRow
{
    string CandidateId { get; }
    DateOnly EventOn { get; set; }
    string[] IssueIds { get; set; }
    string[] OfficialIds { get; set; }
}
