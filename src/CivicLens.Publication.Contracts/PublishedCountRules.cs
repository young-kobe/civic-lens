namespace CivicLens.Publication.Contracts;

/// <summary>The one owner of the fixed numbers that the rules of the counts and the picks use. The rules and the public method text both read these values.</summary>
public static class PublishedCountRules
{
    /// <summary>Days before the saved page in which a statement date must fall for code to approve the statement.</summary>
    public const int StatementDateWindowDays = 7;

    /// <summary>Records an official needs before a page shows a share.</summary>
    public const int MinimumRecordsForShare = 5;

    /// <summary>Days that end on the release day, in which the closest vote is picked.</summary>
    public const int ClosestVoteWindowDays = 7;

    /// <summary>Substantive votes of each chamber in the latest list of the home page.</summary>
    public const int HomeVotesPerChamber = 2;

    /// <summary>Executive actions in the latest list of the home page.</summary>
    public const int HomeExecutiveActionCount = 3;
}
