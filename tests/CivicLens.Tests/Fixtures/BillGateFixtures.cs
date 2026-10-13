using CivicLens.Application.Issues;

namespace CivicLens.Tests.Fixtures;

internal static class BillGateFixtures
{
    public const string TrackedPolicyArea = "Housing and Community Development";
    public const string UntrackedPolicyArea = "Taxation";

    public static IssueConfiguration Issues(DateOnly? startsOn = null, DateOnly? endsBefore = null) => new()
    {
        PolicyAreas =
        [
            new PolicyAreaConfiguration { Id = "housing", Name = TrackedPolicyArea },
            new PolicyAreaConfiguration { Id = "taxes", Name = UntrackedPolicyArea }
        ],
        Issues =
        [
            new TrackedIssueConfiguration
            {
                Id = "rent", Name = "Rent", Definition = "Rent costs.", PolicyAreaIds = ["housing"],
                StartsOn = startsOn, EndsBefore = endsBefore
            }
        ]
    };

    public static IssueConfigurationRevision Revision() => IssueConfigurationRevision.Create(Issues());
}
