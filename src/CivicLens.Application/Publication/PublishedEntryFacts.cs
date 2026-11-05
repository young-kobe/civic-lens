using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>The facts of one entry row and its child rows. The row holds nothing else.</summary>
public sealed record PublishedEntryFacts(string Headline, string? SourceHost, DateOnly? EventDate, string? BillTitle, string? BillLine, PublishedRelationshipType? LinkType,
    string? StatementRecordId, PublishedRecordKind? TargetKind, string? TargetRecordId, PublishedExecutiveActionKind? DocumentType, string[] OfficialIds,
    string? MatchedOfficialId, string[] IssueIds, string[] PolicyAreaIds);
