namespace CivicLens.Application.Votes;

public static class VoteFactPrefixes
{
    public const string Question = "Question: ";
    public const string Legislation = "Legislation: ";
    public const string Title = "Title: ";

    public static readonly string[] Detail = ["Started: ", Question, "Result: ", Legislation, "Party total ", "Candidate total "];
    public static readonly string[] Bill = [Title, "Policy area: "];
}
