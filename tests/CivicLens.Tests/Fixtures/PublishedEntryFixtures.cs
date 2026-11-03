using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Fixtures;

public static class PublishedEntryFixtures
{
    public static PublishedRecordEntry Action(string recordId, DateTimeOffset firstPublishedAt, int revision = 1) => new()
    {
        Kind = PublishedRecordKind.Action,
        RecordId = recordId,
        RevisionNumber = revision,
        Headline = "A bill about housing",
        FirstPublishedAtUtc = firstPublishedAt,
        Officials = [new() { Id = "S000002", Name = "Sam Second" }, new() { Id = "S000001", Name = "Sam First" }],
        IssueIds = ["tax", "housing"],
        PolicyAreaIds = ["economics"],
        SourceHost = "www.congress.gov",
        EventDate = new DateOnly(2026, 3, 1),
        BillTitle = "To fund housing"
    };

    public static PublishedRecordEntry Relationship(string recordId, string statementId, string targetId, DateTimeOffset firstPublishedAt) => new()
    {
        Kind = PublishedRecordKind.Relationship,
        RecordId = recordId,
        RevisionNumber = 1,
        Headline = "A statement about housing",
        FirstPublishedAtUtc = firstPublishedAt,
        Officials = [new() { Id = "S000001", Name = "Sam First" }, new() { Id = "S000002", Name = "Sam Second" }, new() { Id = "S000003", Name = "Sam Third" }],
        IssueIds = ["tax", "housing"],
        PolicyAreaIds = ["economics"],
        EventDate = new DateOnly(2026, 3, 1),
        LinkType = PublishedRelationshipType.DescribesOwnBill,
        StatementRecordId = statementId,
        TargetKind = PublishedRecordKind.Action,
        TargetRecordId = targetId,
        MatchedOfficialId = "S000002"
    };

    public static PublishedRecordEntry Vote(string recordId, DateTimeOffset firstPublishedAt) => new()
    {
        Kind = PublishedRecordKind.Vote,
        RecordId = recordId,
        RevisionNumber = 1,
        Headline = "A vote on housing",
        FirstPublishedAtUtc = firstPublishedAt,
        Officials = [new() { Id = "S000001", Name = "Sam First" }],
        IssueIds = ["housing"],
        PolicyAreaIds = [],
        EventDate = new DateOnly(2026, 3, 1),
        YeaCount = 2,
        NayCount = 1,
        Alignment = PublishedVoteAlignment.PartiesAgree
    };

    public static PublishedRecordEntry ExecutiveAction(string recordId, DateTimeOffset firstPublishedAt) => new()
    {
        Kind = PublishedRecordKind.ExecutiveAction,
        RecordId = recordId,
        RevisionNumber = 1,
        Headline = "An order about housing",
        FirstPublishedAtUtc = firstPublishedAt,
        Officials = [],
        IssueIds = ["housing"],
        PolicyAreaIds = [],
        EventDate = new DateOnly(2026, 3, 1),
        DocumentType = PublishedExecutiveActionKind.ExecutiveOrder
    };

    public static string InsertActionRevisionSql(string recordId, int revision = 1) => $$"""
        SET session_replication_role = replica;
        INSERT INTO action_revisions (record_id, revision_number, author_subject, author_kind, revision_json, created_at_utc_ticks)
        VALUES ('{{recordId}}', {{revision}}, 'owner', 'Person', '{}', 0)
        """;
}
