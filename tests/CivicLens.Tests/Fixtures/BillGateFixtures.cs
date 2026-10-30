using CivicLens.Application.Issues;

namespace CivicLens.Tests.Fixtures;

internal static class BillGateFixtures
{
    public const string TrackedPolicyArea = "Housing and Community Development";
    public const string UntrackedPolicyArea = "Taxation";

    public static IssueConfiguration Issues(DateOnly? startsOn = null, DateOnly? endsBefore = null, bool withEviction = false)
    {
        var rent = new TrackedIssueConfiguration
        {
            Id = "rent",
            Name = "Rent",
            Definition = "Rent costs.",
            PolicyAreaIds = ["housing"],
            StartsOn = startsOn,
            EndsBefore = endsBefore
        };
        var eviction = new TrackedIssueConfiguration
        {
            Id = "eviction",
            Name = "Evictions",
            Definition = "Removal of tenants.",
            PolicyAreaIds = ["housing"]
        };
        return new()
        {
            PolicyAreas =
            [
                new PolicyAreaConfiguration { Id = "housing", Name = TrackedPolicyArea },
                new PolicyAreaConfiguration { Id = "taxes", Name = UntrackedPolicyArea }
            ],
            Issues = withEviction ? [rent, eviction] : [rent]
        };
    }

    public static IssueConfigurationRevision Revision(bool withEviction = false) => IssueConfigurationRevision.Create(Issues(withEviction: withEviction));

    public static BillIssueChoice HousingChoice() =>
        BillIssueChoice.For(TrackedPolicyArea, new DateOnly(2025, 3, 3), Issues());
}
