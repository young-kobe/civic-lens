using System.Globalization;
using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;

namespace CivicLens.Host.Publication;

public static class ReleaseCommand
{
    private const int DefaultListLimit = 20;

    private static readonly JsonSerializerOptions OutputJsonOptions = new(JsonSerializerDefaults.Web);

    public static bool Matches(string[] args) => args is ["releases", ..];

    public static Task<int> ExecuteAsync(string[] args, PublishRelease publish,
        ListPublicationReleases list, ActivatePublicationRelease activate, ReviewActor owner,
        CancellationToken cancellationToken) =>
        OwnerCommand.RunAsync(async () =>
        {
            try
            {
                switch (args)
                {
                    case ["releases", "publish", var key, .. { Length: > 0 } records]:
                        Write(await publish.ExecuteAsync(owner,
                            new PublishReleaseRequest(
                                [.. records.Select(PublishedRecordReference.Parse)], key), cancellationToken));
                        return 0;
                    case ["releases", "list"]:
                        Write(await list.ExecuteAsync(owner, DefaultListLimit, cancellationToken));
                        return 0;
                    case ["releases", "list", var limitText] when TryParsePositive(limitText, out var limit):
                        Write(await list.ExecuteAsync(owner, limit, cancellationToken));
                        return 0;
                    case ["releases", "activate", var numberText] when TryParsePositive(numberText, out var number):
                        Write(await activate.ExecuteAsync(owner, number, cancellationToken));
                        return 0;
                    default:
                        Console.Error.WriteLine("Use releases publish <idempotency-key> <kind>:<id>... (kind is documentChange or statement), releases list [limit], or releases activate <release-number>.");
                        return 2;
                }
            }
            catch (PublicationConflictException exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        });

    private static bool TryParsePositive(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 1;

    private static void Write<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, OutputJsonOptions));
}
