using System.Text.Json;

namespace CivicLens.Application.Publication;

public sealed record ReleaseBillCountFailure(int ReleaseNumber, string Reason, string Detail)
{
    public static ReleaseBillCountFailure Missing(int releaseNumber) =>
        new(releaseNumber, "countsMissing", "The release has no saved bill count rows.");

    public static ReleaseBillCountFailure Different(int releaseNumber) =>
        new(releaseNumber, "countsDifferent", "The saved bill count rows differ from the expected counts.");

    public static bool IsReportable(Exception exception) => exception is IOException or InvalidDataException or JsonException or ArgumentException
        or UnauthorizedAccessException or InvalidOperationException;

    public static ReleaseBillCountFailure Of(int releaseNumber, Exception exception) => new(releaseNumber, exception switch
    {
        FileNotFoundException or DirectoryNotFoundException => "recordMissing",
        InvalidDataException or JsonException or ArgumentException => "recordInvalid",
        IOException or UnauthorizedAccessException => "recordUnreadable",
        _ => "countsInvalid"
    }, exception.Message);
}
