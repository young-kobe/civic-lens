namespace CivicLens.Infrastructure.Review.Persistence;

internal interface IListedDraftRow : IReviewDraftRow
{
    DateOnly EventOn { get; set; }
    string[] IssueIds { get; set; }
    string[] OfficialIds { get; set; }
}
