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
        BillTitle = "To fund housing",
        CosponsorIds = ["C000003", "C000001"]
    };

    public static string InsertActionRevisionSql(string recordId, int revision = 1) => $$"""
        SET session_replication_role = replica;
        INSERT INTO action_revisions (record_id, revision_number, author_subject, author_kind, revision_json, created_at_utc_ticks)
        VALUES ('{{recordId}}', {{revision}}, 'owner', 'Person', '{}', 0)
        """;
}
