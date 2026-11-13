using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

/// <summary>Sample page data and the render call that the statement, executive action, and document change page tests share.</summary>
internal static class DetailPageFixture
{
    public const string Root = "https://civic-lens.test/";

    public static readonly string StatementId = new('5', 64);
    public static readonly string ActionId = new('e', 64);
    public static readonly string ChangeId = new('d', 64);

    public const string StatementText = "Intro line. \"First quote.\" Middle. \"Second quote.\" More. \"Third quote.\" Gap. \"Fourth quote.\" End. Said on Friday.";

    public const string StatementLabel = "An AI summary of this release.";

    public const string ChangeLabel = "Drafted by an AI model from these source versions. A person checked it before publication.";

    /// <summary>A model summary that code approved carries the label of the view. A person summary carries none. Pass <paramref name="label"/> to set the label column on its own.</summary>
    public static StatementPageData Statement(PublishedTextOrigin origin = PublishedTextOrigin.Model, bool dated = true, int quotes = 4,
        string? label = StatementLabel, PublishedApprover approver = PublishedApprover.Code) => new(
        7, StatementId, 2, dated ? new DateOnly(2026, 10, 29) : null, "Kelly announces a bill", "A model wrote this <b>summary</b>.", origin,
        origin == PublishedTextOrigin.Model ? label : null, approver,
        [new("official-a", "Mark Kelly") { Seat = "Senator for AZ", Party = "Democrat" }, new("official-b", "Adam Schiff")], [new("budget", "Federal budget and debt")],
        new PublishedDocumentVersion { ExtractionId = "x1", Url = "https://feed.example/rss", ObservedAtUtc = new(2026, 10, 29, 17, 19, 0, TimeSpan.Zero), Text = StatementText },
        "https://kelly.example/release",
        dated ? new PublishedCitation { ExtractionId = "x1", Start = StatementText.IndexOf("Friday", StringComparison.Ordinal), Length = 6, Quote = "Friday" } : null,
        [.. new[] { "First quote.", "Second quote.", "Third quote.", "Fourth quote." }.Take(quotes).Select(quote =>
            new PublishedQuotation { ExtractionId = "x1", Start = StatementText.IndexOf(quote, StringComparison.Ordinal), Length = quote.Length, Quote = quote, Kind = PublishedQuotationKind.DirectQuotation })],
        new DateTimeOffset(2026, 10, 30, 0, 42, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 30, 0, 44, 0, TimeSpan.Zero));

    public static ExecutiveActionPageData Action(params ExecutiveActionNoticeData[] notices) => new(
        7, ActionId, 1, "Implementing the Sanctioning Russia and Iran Act", PublishedExecutiveActionKind.ExecutiveOrder, "2026-21599",
        new(2026, 10, 16), new(2026, 10, 21), "91 FR 66627", "https://www.federalregister.gov/d/2026-21599", "president", "Donald Trump", notices, []);

    public const string BeforeText = "Visitors must book a tour at least seven days before the visit and bring a photo ID.";
    public const string AfterText = "Visitors must book a tour at least three days before the visit and bring a photo ID.";

    public static PublishedChange SevenToThree()
    {
        var start = BeforeText.IndexOf("seven", StringComparison.Ordinal);
        var afterStart = AfterText.IndexOf("three", StringComparison.Ordinal);
        return new()
        {
            BeforeStart = start,
            BeforeLength = 5,
            AfterStart = afterStart,
            AfterLength = 5,
            BeforeContextStart = 0,
            BeforeContextLength = BeforeText.Length,
            AfterContextStart = 0,
            AfterContextLength = AfterText.Length,
            WordEdits = [new() { BeforeStart = start, BeforeLength = 5, AfterStart = afterStart, AfterLength = 5 }]
        };
    }

    public static DocumentChangePageData Change(PublishedTextOrigin origin = PublishedTextOrigin.Model, DateOnly? changeDate = null, int passages = 1,
        int citations = 2, string? label = ChangeLabel) => new(
        7, ChangeId, 1, "Example Agency changed its visitor policy page", "The agency cut the notice from seven days to three.", origin,
        origin == PublishedTextOrigin.Model ? label : null, "A shorter notice helps visitors.", "The text alone does not show the reason.", "Example Agency", changeDate,
        [new("official-a", "Ann Able")], [new("budget", "Federal budget and debt")],
        new PublishedDocumentVersion { ExtractionId = "b1", Url = "https://example.gov/visit", ObservedAtUtc = new(2026, 10, 12, 8, 0, 0, TimeSpan.Zero), Text = BeforeText },
        new PublishedDocumentVersion { ExtractionId = "a1", Url = "https://example.gov/visit", ObservedAtUtc = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero), Text = AfterText },
        [.. Enumerable.Repeat(SevenToThree(), passages)],
        [.. new[] { new PublishedCitation { ExtractionId = "a1", Start = AfterText.IndexOf("three", StringComparison.Ordinal), Length = 5, Quote = "three" },
            new PublishedCitation { ExtractionId = "b1", Start = BeforeText.IndexOf("tour", StringComparison.Ordinal), Length = 4, Quote = "tour" }}.Take(citations)],
        changeDate is null ? null : new PublishedCitation { ExtractionId = "a1", Start = AfterText.IndexOf("photo", StringComparison.Ordinal), Length = 5, Quote = "photo" });

    public static readonly string RelationshipId = new('7', 64);

    /// <summary>A relationship between the sample statement and a bill. A model reason carries the label of the view. The bill fact is model text with its own label.</summary>
    public static RelationshipPageData Relationship(PublishedTextOrigin origin = PublishedTextOrigin.Model, DateOnly? statementDate = null, bool dated = true) => new(
        7, RelationshipId, 2, "Kelly announces a bill and Drain the Slush Fund Act", PublishedRelationshipType.DescribesOwnBill,
        dated ? statementDate ?? new DateOnly(2026, 10, 29) : null, "Kelly says he introduced this <b>bill</b>.", origin,
        origin == PublishedTextOrigin.Model ? PublishedWording.ModelRelationship : null,
        new(PublishedRecordKind.Statement, StatementId, 2, "Kelly announces a bill", PublishedTextOrigin.Model, StatementLabel),
        new(PublishedRecordKind.Action, ActionId, 1, "Drain the Slush Fund Act", PublishedTextOrigin.Code, null),
        "\u201cI introduced the Drain the Slush Fund Act.\u201d", "Summary: The bill shuts down the fund.", "An AI summary of the official record",
        [new("official-a", "Mark Kelly")], [new("budget", "Federal budget and debt")],
        new DateTimeOffset(2026, 10, 30, 0, 42, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 30, 0, 44, 0, TimeSpan.Zero));

    public static async Task<IDocument> RenderAsync<TPage, TData>(TData data) where TPage : IComponent
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TPage>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["Data"] = data, ["RootPath"] = Root }));
            return output.ToHtmlString();
        });
        return await new HtmlParser().ParseDocumentAsync(html);
    }
}
