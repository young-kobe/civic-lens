using System.Text.Json;
using CivicLens.Application.Actions;
using CivicLens.Application.Collection.Bills;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;

namespace CivicLens.Tests.Fixtures;

/// <summary>The official text of the bill in <see cref="ActionBillWorld"/>, as the text job stores it, and the model answers that quote it.</summary>
internal static class ActionBillTexts
{
    public const string PackageId = "BILLS-119hr1ih";
    public const string FirstSummary = "The Secretary shall pay landlords on behalf of tenants with low income.";
    public const string SecondSummary = "A tenant is eligible when the household income is under 50 percent of the area median.";
    public const string WhyItMatters = "The program ends on December 31, 2030.";
    public const string FirstQuote = "pay landlords on behalf of tenants with low income";
    public const string SecondQuote = "when the household income is under 50 percent of the area median";
    public const string WhyQuote = "program ends on December 31, 2030";
    public const string FirstPlain = "The bill lets the Secretary pay landlords on behalf of tenants with low income.";
    public const string SecondPlain = "A tenant can get help when the home earns less than half of the area median.";
    public const string WhyPlain = "Aid under the bill stops when the program ends on December 31, 2030.";
    public const string ExtractionVersion = "congress-bill-text-v1+fake-extraction";

    private static readonly CapturedAttemptResult Attempt = new(new string('7', 32), "congress-bill-text",
        "https://api.govinfo.gov/packages/BILLS-119hr1ih/htm", "https://api.govinfo.gov/packages/BILLS-119hr1ih/htm", DateTimeOffset.UnixEpoch,
        new CollectionResponse(200, null, null, "text/html", []), new CaptureIdentity(new string('a', 64), 0));

    public static readonly string ExtractionId = new DocumentExtraction(Attempt, ExtractionVersion, "text").ExtractionId;

    public static readonly string Body = $"Section 1. This Act may be cited as the Rent Aid Act. {FirstSummary} {SecondSummary} {WhyItMatters}";

    public static ActionDraftBillText Of(string? body = null, string extractionVersion = ExtractionVersion)
    {
        var type = new CongressBillTextVersion(PackageId, "Introduced in House", new DateTimeOffset(2025, 1, 3, 5, 0, 0, TimeSpan.Zero),
            $"https://www.congress.gov/119/bills/hr1/{PackageId}.htm");
        var reading = CongressBillRenderer.RenderText(new CongressBillListItem(119, "hr", 1, default), type, body ?? Body);
        return new(PackageId, type.Type, new DateOnly(2025, 1, 3), ExtractionId, extractionVersion, reading.Text, reading.CharsRead, reading.CharsTotal);
    }

    public static DocumentExtraction Extraction(ActionDraftBillText text) => new(Attempt, ExtractionVersion, text.Text);

    public static string Output((string Sentence, string Quote)[]? summary = null, (string Sentence, string Quote)[]? whyItMatters = null,
        string[]? issueIds = null) => JsonSerializer.Serialize(new
        {
            headline = new { text = "H.R. 1 pays landlords for tenants with low income.", confidence = 0.8 },
            summary = new { sentences = (summary ?? [(FirstPlain, FirstQuote), (SecondPlain, SecondQuote)]).Select(Item), confidence = 0.7 },
            whyItMatters = new { sentences = (whyItMatters ?? [(WhyPlain, WhyQuote)]).Select(Item), confidence = 0.6 },
            issueIds = issueIds ?? ["rent"]
        });

    private static object Item((string Sentence, string Quote) item) => new { sentence = item.Sentence, quote = item.Quote };
}
