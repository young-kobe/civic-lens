using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.PublicApi;

/// <summary>A record file with the manifest entry that lists it.</summary>
internal sealed record SampleRecord(PublishedRecordEntry Entry, object Body);

/// <summary>Small valid records of every kind. Each takes the texts that a search test needs.</summary>
internal static class PublishedSamples
{
    private static readonly DateTimeOffset Published = new(2026, 5, 11, 8, 0, 0, TimeSpan.Zero);
    private static readonly string SourceId = new('b', 64);

    public static SampleRecord Statement(char id, string headline, string summary = "A short account.", string source = "Xq the source text.",
        int revision = 1, DateOnly? date = null, string[]? issues = null, string[]? areas = null, string? official = "o1",
        string host = "example.gov", DateTimeOffset? published = null)
    {
        var record = new PublishedStatement
        {
            Corrections = [],
            RecordId = new string(id, 64),
            RevisionNumber = revision,
            Headline = headline,
            Summary = summary,
            StatementDate = date,
            StatementDateEvidence = date is null ? null : new PublishedCitation { ExtractionId = SourceId, Start = 0, Length = 2, Quote = source[..2] },
            Speakers = official is null ? [] : [new PublishedOfficial { Id = official, Name = "Official " + official }],
            OtherSpeakers = official is null ? ["An aide"] : [],
            IssueIds = issues ?? [],
            PolicyAreaIds = areas ?? [],
            BillNumbers = [],
            Quotations = [new PublishedQuotation { ExtractionId = SourceId, Start = 0, Length = 2, Quote = source[..2], Kind = PublishedQuotationKind.DirectQuotation }],
            Source = new PublishedDocumentVersion { ExtractionId = SourceId, Url = $"https://{host}/story", ObservedAtUtc = Published, Text = source },
            ApprovedAtUtc = Published,
            FirstPublishedAtUtc = published ?? Published
        };
        return new(record.ToEntry(), record);
    }

    public static SampleRecord DocumentChange(string headline, string summary, string after, string before = "Old text.")
    {
        const string beforeId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var record = new PublishedDocumentChange
        {
            Corrections = [],
            RecordId = new string('c', 32),
            RevisionNumber = 1,
            Headline = headline,
            Summary = summary,
            Institution = "Senate",
            Officials = [],
            IssueIds = [],
            Before = new PublishedDocumentVersion { ExtractionId = beforeId, Url = "https://example.gov/before", ObservedAtUtc = Published, Text = before },
            After = new PublishedDocumentVersion { ExtractionId = SourceId, Url = "https://example.gov/after", ObservedAtUtc = Published, Text = after },
            Changes =
            [
                new PublishedChange
                {
                    BeforeStart = 0, BeforeLength = 1, AfterStart = 0, AfterLength = 1,
                    BeforeContextStart = 0, BeforeContextLength = 1, AfterContextStart = 0, AfterContextLength = 1, WordEdits = []
                }
            ],
            Citations =
            [
                new PublishedCitation { ExtractionId = beforeId, Start = 0, Length = 1, Quote = before[..1] },
                new PublishedCitation { ExtractionId = SourceId, Start = 0, Length = 1, Quote = after[..1] }
            ],
            ApprovedAtUtc = Published,
            FirstPublishedAtUtc = Published
        };
        return new(record.ToEntry(), record);
    }

    public static SampleRecord Action(string headline, string summary, string billTitle, string source)
    {
        var record = new PublishedAction
        {
            Corrections = [],
            RecordId = new string('f', 64),
            RevisionNumber = 1,
            Headline = headline,
            Summary = summary,
            TextOrigin = PublishedTextOrigin.Model,
            Congress = 119,
            BillType = "hr",
            BillNumber = 7,
            BillTitle = billTitle,
            TrackedIssueIds = [],
            Sponsorships =
            [
                new PublishedSponsorship
                {
                    Official = new PublishedOfficial { Id = "o1", Name = "Official o1" },
                    Role = PublishedSponsorshipRole.Sponsor,
                    IsOriginal = true,
                    SponsoredOn = new DateOnly(2026, 1, 3),
                    Citations = [new PublishedCitation { ExtractionId = SourceId, Start = 0, Length = 2, Quote = source[..2] }]
                }
            ],
            Sources = [new PublishedDocumentVersion { ExtractionId = SourceId, Url = "https://api.congress.gov/v3/bill/119/hr/7", ObservedAtUtc = Published, Text = source }],
            ApprovedAtUtc = Published,
            FirstPublishedAtUtc = Published
        };
        return new(record.ToEntry(), record);
    }

    public static SampleRecord Vote(string headline, string summary, string source)
    {
        var started = "Started: 2025-04-10T11:08:00-04:00";
        var member = "Member: A000055, Jane Doe, R, AL, Yea";
        var text = started + "\n" + member + "\n" + source;
        PublishedCitation Cite(string quote) => new()
        {
            ExtractionId = SourceId,
            Start = text.IndexOf(quote, StringComparison.Ordinal),
            Length = quote.Length,
            Quote = quote
        };
        var record = new PublishedVote
        {
            Corrections = [],
            RecordId = new string('e', 64),
            RevisionNumber = 1,
            Headline = headline,
            Summary = summary,
            TextOrigin = PublishedTextOrigin.Model,
            Congress = 119,
            Session = 1,
            RollCallNumber = 100,
            Kind = PublishedVoteKind.Other,
            VotedOn = new DateOnly(2025, 4, 10),
            VotedOnCitation = Cite(started),
            Facts = [],
            Members = [new PublishedMemberVote { Official = new PublishedOfficial { Id = "o1", Name = "Official o1" }, Cast = "Yea", Citation = Cite(member) }],
            TrackedIssueIds = [],
            Sources = [new PublishedDocumentVersion { ExtractionId = SourceId, Url = "https://api.congress.gov/v3/house-vote/119/1/100", ObservedAtUtc = Published, Text = text }],
            ApprovedAtUtc = Published,
            FirstPublishedAtUtc = Published
        };
        return new(record.ToEntry(), record);
    }

    public static SampleRecord Brief(string headline, string paragraph)
    {
        var record = new PublishedBrief
        {
            Corrections = [],
            RecordId = CivicLens.Core.Briefs.BriefWeek.RecordIdOf(new DateOnly(2026, 10, 5)),
            RevisionNumber = 1,
            Headline = headline,
            WeekStart = new DateOnly(2026, 10, 5),
            CutoffReleaseNumber = 3,
            Coverage = new() { Considered = 2, SentToModel = 1, Cited = 1 },
            Paragraphs = [new() { Text = paragraph, CitationIds = ["R1"] }],
            Citations = [new() { Id = "R1", Kind = PublishedRecordKind.Statement, RecordId = new string('1', 64), RevisionNumber = 1, Headline = "Cited statement" }],
            ApprovedAtUtc = Published,
            FirstPublishedAtUtc = Published
        };
        return new(record.ToEntry(), record);
    }

    public static SampleRecord Relationship(string headline, string reason, string quote)
    {
        var record = new PublishedRelationship
        {
            Corrections = [],
            RecordId = new string('9', 64),
            RevisionNumber = 1,
            Headline = headline,
            Statement = new() { Kind = PublishedRecordKind.Statement, RecordId = new string('1', 64), RevisionNumber = 1, Headline = "A statement" },
            Target = new() { Kind = PublishedRecordKind.Action, RecordId = new string('f', 64), RevisionNumber = 1, Headline = "An action" },
            Type = PublishedRelationshipType.DescribesOwnBill,
            Reason = reason,
            Quote = quote,
            TargetFact = "Title: A bill",
            Officials = [],
            IssueIds = [],
            PolicyAreaIds = [],
            ApprovedAtUtc = Published,
            FirstPublishedAtUtc = Published
        };
        return new(record.ToEntry(), record);
    }
}
