namespace CivicLens.Infrastructure.Publication.Persistence;

internal abstract class PublishedEntryValueRow : PublishedEntryChildRow
{
    public string Value { get; set; } = string.Empty;
}

internal sealed class PublishedEntryIssueRow : PublishedEntryValueRow;

internal sealed class PublishedEntryPolicyAreaRow : PublishedEntryValueRow;

internal sealed class PublishedEntryCosponsorRow : PublishedEntryValueRow;
