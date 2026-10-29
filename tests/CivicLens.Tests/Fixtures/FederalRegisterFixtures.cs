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

    public const string BidenProclamation = """
        {"document_number":"2024-26714","type":"Presidential Document","subtype":"Proclamation","title":"To Facilitate Positive Adjustment to Competition From Imports of Fine Denier Polyester Staple Fiber","signing_date":"2024-11-08","publication_date":"2024-11-13","citation":"89 FR 89591","president":{"identifier":"joe-biden","name":"Joseph R. Biden Jr."},"correction_of":null,"presidential_document_number":"10857","html_url":"https://www.federalregister.gov/documents/2024/11/13/2024-26714/to-facilitate-positive-adjustment-to-competition-from-imports-of-fine-denier-polyester-staple-fiber"}
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

    public static byte[] Bytes(string json) => Encoding.UTF8.GetBytes(json);

    public static FederalRegisterDocument Document(string json) =>
        FederalRegisterDocumentParser.Parse(Bytes(json), NumberOf(json));

    public static string NumberOf(string json)
    {
        const string key = "\"document_number\":\"";
        var start = json.IndexOf(key, StringComparison.Ordinal) + key.Length;
        return json[start..json.IndexOf('"', start)];
    }

    public static CollectionConfiguration Configuration(DiscoveryAdmissionPolicy? policy = null) => new()
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
