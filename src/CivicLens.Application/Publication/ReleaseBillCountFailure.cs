using System.Text.Json;

namespace CivicLens.Application.Publication;

public sealed record ReleaseBillCountFailure(int ReleaseNumber, string Reason, string Detail)
{
    public static ReleaseBillCountFailure Missing(int releaseNumber, ReleaseSnapshotKind kind) =>
        new(releaseNumber, $"{kind.ReasonPrefix}Missing", $"The release has no saved {kind.Label} rows.");

    public static ReleaseBillCountFailure Different(int releaseNumber, ReleaseSnapshotKind kind) =>
        new(releaseNumber, $"{kind.ReasonPrefix}Different", $"The saved {kind.Label} rows differ from the expected rows.");

    public static bool IsReportable(Exception exception) => exception is IOException or InvalidDataException or JsonException or ArgumentException
        or UnauthorizedAccessException or BillCountRefusedException;

    public static ReleaseBillCountFailure Of(int releaseNumber, Exception exception, ReleaseSnapshotKind kind) => new(releaseNumber, exception switch
    {
        FileNotFoundException or DirectoryNotFoundException => "recordMissing",
        InvalidDataException or JsonException or ArgumentException => "recordInvalid",
        IOException or UnauthorizedAccessException => "recordUnreadable",
        _ => $"{kind.ReasonPrefix}Invalid"
    }, exception.Message);
}
