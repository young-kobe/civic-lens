using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Votes;

public static class VoteFactPrefixes
{
    public const string Started = "Started: ";
    public const string Question = "Question: ";
    public const string Legislation = "Legislation: ";
    public const string Title = "Title: ";
    public const string Result = PublishedVote.ResultPrefix;
    public const string PartyTotal = "Party total ";
    public const string Count = "Count: ";
    public const string CandidateTotal = PublishedVote.CandidateTotalPrefix;

    public static readonly string[] SenateDetail =
        [Started, Question, "Question text: ", "Vote title: ", Result, "Document: ", Count];
    public static readonly string[] Detail = [Started, Question, Result, Legislation, PartyTotal, CandidateTotal];
    public static readonly string[] Bill = [Title, "Policy area: "];
}
