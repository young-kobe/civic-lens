using System.Text;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;

namespace CivicLens.Tests.Fixtures;

/// <summary>Trimmed copies of Federal Register API answers, fetched 2026-10-29 with the pinned fields of <see cref="FederalRegisterUrl.Document"/>.</summary>
internal static class FederalRegisterFixtures
{
    public const string SourceId = "federal-register-documents";
    public const string SourceUrl = "https://www.federalregister.gov/api/v1/documents.json";
    public static readonly DateOnly Backfill = new(2024, 10, 29);

    public const string TrumpOrder = """
        {"document_number":"2025-01900","type":"Presidential Document","subtype":"Executive Order","title":"Ending the Weaponization of the Federal Government","signing_date":"2025-01-20","publication_date":"2025-01-28","citation":"90 FR 8235","president":{"identifier":"donald-trump","name":"Donald Trump"},"correction_of":null,"presidential_document_number":"14147","html_url":"https://www.federalregister.gov/documents/2025/01/28/2025-01900/ending-the-weaponization-of-the-federal-government"}
        """;

    /// <summary>The fields of the 2026-10-29 fetch, plus the <c>body_html_url</c> of <see cref="ProclamationBodyUrl"/>.</summary>
    public const string BidenProclamation = """
        {"document_number":"2024-26714","type":"Presidential Document","subtype":"Proclamation","title":"To Facilitate Positive Adjustment to Competition From Imports of Fine Denier Polyester Staple Fiber","signing_date":"2024-11-08","publication_date":"2024-11-13","citation":"89 FR 89591","president":{"identifier":"joe-biden","name":"Joseph R. Biden Jr."},"correction_of":null,"presidential_document_number":"10857","html_url":"https://www.federalregister.gov/documents/2024/11/13/2024-26714/to-facilitate-positive-adjustment-to-competition-from-imports-of-fine-denier-polyester-staple-fiber","body_html_url":"https://www.federalregister.gov/documents/full_text/html/2024/11/14/2024-26714.html"}
        """;

    public const string BidenProclamationCorrection = """
        {"document_number":"R1-2024-26714","type":"Presidential Document","subtype":"Proclamation","title":"To Facilitate Positive Adjustment to Competition From Imports of Fine Denier Polyester Staple Fiber","signing_date":"2024-11-08","publication_date":"2024-11-20","citation":"89 FR 92015","president":{"identifier":"joe-biden","name":"Joseph R. Biden Jr."},"correction_of":"https://www.federalregister.gov/api/v1/documents/2024-26714","presidential_document_number":"10857","html_url":"https://www.federalregister.gov/documents/2024/11/20/R1-2024-26714/to-facilitate-positive-adjustment-to-competition-from-imports-of-fine-denier-polyester-staple-fiber"}
        """;

    public const string BidenNotice = """
        {"document_number":"2024-25777","type":"Presidential Document","subtype":"Notice","title":"Continuation of the National Emergency With Respect to Iran","signing_date":"2024-11-01","publication_date":"2024-11-04","citation":"89 FR 87761","president":{"identifier":"joe-biden","name":"Joseph R. Biden Jr."},"correction_of":null,"presidential_document_number":null,"html_url":"https://www.federalregister.gov/documents/2024/11/04/2024-25777/continuation-of-the-national-emergency-with-respect-to-iran"}
        """;

    public const string List = """
        {"description":"Documents published from 01/28/2025 to 01/28/2025, of type Presidential Document, and of presidential document type Executive Order","count":3,"total_pages":1,"results":[{"document_number":"2025-01900","publication_date":"2025-01-28"},{"document_number":"2025-01901","publication_date":"2025-01-28"},{"document_number":"2025-01902","publication_date":"2025-01-28"}]}
        """;

    public const string TextSourceId = "federal-register-text";
    public const string TextSourceUrl = "https://www.federalregister.gov/documents/full_text/html/";

    /// <summary>Real <c>body_html_url</c> values that the owner session fetched from the API detail response on 2026-11-12. That fetch gave
    /// 2024-26714 the publication_date 2024-11-14. <see cref="BidenProclamation"/> keeps the 2024-11-13 of its 2026-10-29 fetch exactly as captured. The owner decided on 2026-11-12 to keep it,
    /// because it is the real case where the pinned URL date differs from the stored publication date.</summary>
    public const string ProclamationBodyUrl = "https://www.federalregister.gov/documents/full_text/html/2024/11/14/2024-26714.html";

    /// <summary>Document 2025-01953, publication_date 2025-01-29, from the same 2026-11-12 fetch.</summary>
    public const string OrderBodyNumber = "2025-01953";
    public static readonly DateOnly OrderBodyPublishedOn = new(2025, 1, 29);
    public const string OrderBodyUrl = "https://www.federalregister.gov/documents/full_text/html/2025/01/29/2025-01953.html";

    /// <summary>The structure of the HTML body of an executive order at body_html_url, as the owner session measured it on 2026-11-11 for
    /// 2026-22051 (about 13,500 bytes, about 8,000 characters of text). The words are shortened and invented. The markup is the real shape: plain
    /// p, h1, div, em, strong, a, img, span and svg, two kinds of printed page marker, an end-matter block, and a signature image.
    /// The page marker text "printed page 68007" must never reach the extracted text.</summary>
    public const string OrderBodyHtml = """
        <html><body>
        <h1 id="h-1">Executive Order 14999 of October 27, 2026</h1>
        <p>Strengthening the Test of Page Markers</p>
        <span class="printed-page-details unprinted-element document-markup" data-page="68007"><svg viewBox="0 0 8 8"><path d="M0 0"/></svg></span>
        <p>By the authority vested in me as President, it is hereby ordered as follows:</p>
        <p><strong>Section 1</strong>. <em>Policy</em>. It is the policy of the United States to read the <a href="https://example.gov/rule">rule</a> in full.
        <span class="printed-page-inline unprinted-element document-markup" data-page="68007">(<svg viewBox="0 0 8 8"><path d="M0 0"/></svg> printed page 68007)</span>
        Each agency shall report in 30 days.</p>
        <div class="signature"><img class="document-graphic-image" src="https://www.federalregister.gov/signature.gif" alt="Signature"/></div>
        <div class="end-matter"><p>[FR Doc. 2026-22051 Filed 10-28-26; 8:45 am]</p><p>Billing code 3395-F4-P</p></div>
        </body></html>
        """;

    /// <summary>The text of <see cref="OrderBodyHtml"/> with each page marker removed.</summary>
    public const string OrderBodyText = "Executive Order 14999 of October 27, 2026 Strengthening the Test of Page Markers " +
        "By the authority vested in me as President, it is hereby ordered as follows: Section 1. Policy. " +
        "It is the policy of the United States to read the rule in full. Each agency shall report in 30 days.";

    /// <summary>The end-matter block stays in the text. The bill text extractor drops no administrative line either.</summary>
    public const string OrderEndMatterText = "[FR Doc. 2026-22051 Filed 10-28-26; 8:45 am] Billing code 3395-F4-P";

    public static byte[] Bytes(string json) => Encoding.UTF8.GetBytes(json);

    /// <summary>Adds a <c>body_html_url</c> to a fixture that has none. A test that needs more pinned documents than the real values uses it.</summary>
    public static string WithBodyUrl(string json, string bodyUrl) =>
        json.Replace("\"html_url\":", $"\"body_html_url\":\"{bodyUrl}\",\"html_url\":", StringComparison.Ordinal);

    public static FederalRegisterDocument Document(string json) =>
        FederalRegisterDocumentParser.Parse(Bytes(json), NumberOf(json));

    public static string NumberOf(string json)
    {
        const string key = "\"document_number\":\"";
        var start = json.IndexOf(key, StringComparison.Ordinal) + key.Length;
        return json[start..json.IndexOf('"', start)];
    }

    public static CollectionConfiguration Configuration(DiscoveryAdmissionPolicy? policy = null, bool withTextSource = false)
    {
        var configuration = DocumentsConfiguration(policy);
        return withTextSource ? configuration with { Sources = [.. configuration.Sources, TextSource()] } : configuration;
    }

    public static WatchedSourceConfiguration TextSource() => new()
    {
        Id = TextSourceId,
        Url = TextSourceUrl,
        AllowedOrigin = "https://www.federalregister.gov",
        AllowedPathPrefix = "/documents/full_text/html/",
        Mode = CollectionMode.Api,
        ApiParser = ApiParser.FederalRegisterText,
        CheckIntervalSeconds = 86_400,
        AdmissionPolicy = new DiscoveryAdmissionPolicy { MaxJobs = 1, MaxTotalRequests = 4, MaxTotalBytes = 4_000_000, MaxTotalTimeoutSeconds = 60 },
        JobPolicy = new CollectionJobPolicy { MaxAttempts = 2 },
        MaxRequests = 2,
        TimeoutSeconds = 30
    };

    private static CollectionConfiguration DocumentsConfiguration(DiscoveryAdmissionPolicy? policy) => new()
    {
        Version = 1,
        People =
        [
            new PersonConfiguration { Id = "donald-trump", Name = "Donald Trump", Offices = [President(2025, 1, 20, 2029, 1, 20)] },
            new PersonConfiguration { Id = "joe-biden", Name = "Joe Biden", Offices = [President(2021, 1, 20, 2025, 1, 20)] }
        ],
        Sources =
        [
            new WatchedSourceConfiguration
            {
                Id = SourceId,
                Url = SourceUrl,
                AllowedOrigin = "https://www.federalregister.gov",
                AllowedPathPrefix = "/api/v1/",
                Mode = CollectionMode.Api,
                ApiParser = ApiParser.FederalRegisterDocuments,
                BackfillFrom = Backfill,
                CheckIntervalSeconds = 21_600,
                AdmissionPolicy = policy ?? new DiscoveryAdmissionPolicy
                {
                    MaxJobs = 1000,
                    MaxTotalRequests = 4000,
                    MaxTotalBytes = 400_000_000,
                    MaxTotalTimeoutSeconds = 30_000
                },
                JobPolicy = new CollectionJobPolicy { MaxAttempts = 2 },
                MaxRequests = 2,
                MaxBytes = 200_000,
                TimeoutSeconds = 15
            }
        ]
    };

    private static OfficeConfiguration President(int startYear, int startMonth, int startDay, int endYear, int endMonth, int endDay) => new()
    {
        Title = "President",
        StartsOn = new DateOnly(startYear, startMonth, startDay),
        EndsOn = new DateOnly(endYear, endMonth, endDay),
        Kind = ConfiguredOfficeKind.Executive
    };
}
