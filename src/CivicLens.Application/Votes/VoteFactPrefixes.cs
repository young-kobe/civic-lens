using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Votes;

public static class VoteFactPrefixes
{
    public const string Started = "Started: ";
    public const string Question = PublishedVote.QuestionPrefix;
    public const string Legislation = PublishedVote.LegislationPrefix;
    public const string Title = PublishedVote.TitlePrefix;
    public const string Result = PublishedVote.ResultPrefix;
    public const string PartyTotal = PublishedVote.PartyTotalPrefix;
    public const string Count = PublishedVote.CountPrefix;
    public const string CandidateTotal = PublishedVote.CandidateTotalPrefix;

    public static readonly string[] SenateDetail =
        [Started, Question, "Vote title: ", Result, Count];
    public static readonly string[] Detail = [Started, Question, Result, Legislation, PartyTotal, CandidateTotal];
    public static readonly string[] Bill = [Title, "Policy area: "];
}
