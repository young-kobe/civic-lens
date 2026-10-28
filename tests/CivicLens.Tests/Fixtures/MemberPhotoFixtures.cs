using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.MemberPhotos;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection;
using static CivicLens.Tests.Fixtures.CollectionFixtures;

namespace CivicLens.Tests.Fixtures;

internal static class MemberPhotoFixtures
{
    public const string SourceId = "member-photos";
    public const string SourceUrl = "https://unitedstates.github.io/images/congress/225x275/";
    public const string Bioguide = "A000001";

    public static string PhotoUrl(string bioguideId = Bioguide) => MemberPhotoUrl.Photo(SourceUrl, bioguideId);

    public static WatchedSourceConfiguration Source() => new()
    {
        Id = SourceId,
        Url = SourceUrl,
        AllowedOrigin = "https://unitedstates.github.io",
        AllowedPathPrefix = "/images/congress/225x275/",
        Mode = CollectionMode.Api,
        ApiParser = ApiParser.MemberPhotos,
        MaxRequests = 2,
        MaxBytes = 500_000,
        TimeoutSeconds = 20,
        AdmissionPolicy = new DiscoveryAdmissionPolicy { MaxJobs = 20, MaxTotalRequests = 80 }
    };

    public static CollectionConfiguration Configuration(params PersonConfiguration[] people) =>
        RosterFixtures.Configuration(people) with { Sources = [RosterFixtures.Source(), Source()] };

    /// <summary>A minimal JPEG: start of image, one JFIF segment, a baseline frame header, end of image.</summary>
    public static byte[] Jpeg(int width = 225, int height = 275) =>
    [
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
        0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width,
        0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
        0xFF, 0xD9
    ];

    public static async Task<string> ImportCaptureAsync(PostgresCollectionAttemptStore attempts, string captureSha256,
        DateTimeOffset observedAt, string bioguideId = Bioguide)
    {
        var attemptId = Guid.NewGuid().ToString("N");
        var request = Request() with
        {
            SourceId = SourceId,
            Url = PhotoUrl(bioguideId),
            AllowedOrigin = "https://unitedstates.github.io",
            AllowedPathPrefix = "/images/congress/225x275/"
        };
        var result = Receipt(request) with
        {
            ObservedAt = observedAt,
            Capture = new CaptureArtifact { Sha256 = captureSha256, RelativePath = captureSha256 + ".gz", ByteLength = 4 }
        };
        await attempts.ImportAtomicallyAsync(CollectionAttemptImporter.CreateImport(attemptId, request, result), CancellationToken.None);
        return attemptId;
    }
}
