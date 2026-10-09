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

    public static async Task<int> ExecuteAsync(string[] args, PublishDocumentChanges publish,
        ListPublicationReleases list, ActivatePublicationRelease activate, ReviewActor owner,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (args)
            {
                case ["releases", "publish", var key, .. { Length: > 0 } draftIds]:
                    Write(await publish.ExecuteAsync(owner,
                        new PublishDocumentChangesRequest([.. draftIds], key), cancellationToken));
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
                    Console.Error.WriteLine("Use releases publish <idempotency-key> <draft-id>..., releases list [limit], or releases activate <release-number>.");
                    return 2;
            }
        }
        catch (PublicationConflictException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine("The configured actor is not the owner.");
            return 2;
        }
    }

    private static bool TryParsePositive(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 1;

    private static void Write<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, OutputJsonOptions));
}
