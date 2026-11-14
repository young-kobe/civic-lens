using CivicLens.Application.Issues;
using CivicLens.Application.Publication;

namespace CivicLens.Tests.Fixtures;

internal static class PublicationCatalogs
{
    public static PublicationCatalog None { get; } = new(new Dictionary<string, string>(), IssueConfiguration.None);
}
