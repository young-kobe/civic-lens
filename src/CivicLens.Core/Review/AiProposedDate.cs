namespace CivicLens.Core.Review;

public static class AiProposedDate
{
    public static bool IsUnconfirmedCarryOver(string previousAuthorSubject, DateOnly? previousDate, DateOnly? date, bool dateChecked) =>
        ReviewAuthor.IsAnalysis(previousAuthorSubject) && previousDate is not null && date == previousDate && !dateChecked;

    public static void RequireConfirmed(string authorSubject, DateOnly? date, string dateName)
    {
        if (ReviewAuthor.IsAnalysis(authorSubject) && date is not null)
            throw new ArgumentException($"A person must confirm the AI-proposed {dateName} by saving a revision before approval.");
    }
}
