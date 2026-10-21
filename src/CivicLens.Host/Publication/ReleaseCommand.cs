using System.Globalization;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;

namespace CivicLens.Host.Publication;

public static class ReleaseCommand
{
    private const int DefaultListLimit = 20;
    private const string NoteOption = "--note";
    private const string WithdrawOption = "--withdraw";

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
                    case ["releases", "publish", var key, .. { Length: > 0 } arguments]:
                        CommandOutput.Write(await publish.ExecuteAsync(owner, ParsePublish(key, arguments), cancellationToken));
                        return 0;
                    case ["releases", "list"]:
                        CommandOutput.Write(await list.ExecuteAsync(owner, DefaultListLimit, cancellationToken));
                        return 0;
                    case ["releases", "list", var limitText] when TryParsePositive(limitText, out var limit):
                        CommandOutput.Write(await list.ExecuteAsync(owner, limit, cancellationToken));
                        return 0;
                    case ["releases", "activate", var numberText] when TryParsePositive(numberText, out var number):
                        CommandOutput.Write(await activate.ExecuteAsync(owner, number, cancellationToken));
                        return 0;
                    default:
                        Console.Error.WriteLine($"Use releases publish <idempotency-key> [<kind>:<id>...] [--note <kind>:<id> <text>]... [--withdraw <kind>:<id> <text>]... (kind is one of: {PublishedRecordReference.KindNames}), releases list [limit], or releases activate <release-number>.");
                        return 2;
                }
            }
            catch (PublicationConflictException exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        });

    private static PublishReleaseRequest ParsePublish(string key, string[] arguments)
    {
        List<PublishedRecordReference> records = [];
        List<RecordNote> notes = [];
        List<RecordNote> withdrawals = [];
        for (var index = 0; index < arguments.Length; index++)
        {
            var option = arguments[index];
            if (option is not (NoteOption or WithdrawOption))
            {
                records.Add(PublishedRecordReference.Parse(option));
                continue;
            }
            if (index + 2 >= arguments.Length)
                throw new ArgumentException($"Give {option} as {option} <kind>:<id> <text>.");
            var reference = PublishedRecordReference.Parse(arguments[index + 1]);
            (option == NoteOption ? notes : withdrawals).Add(new(reference.Kind, reference.RecordId, arguments[index + 2]));
            index += 2;
        }
        if (records.Count == 0 && withdrawals.Count == 0)
            throw new ArgumentException($"Name at least one record as <kind>:<id>, or give {WithdrawOption}.");
        return new PublishReleaseRequest([.. records], key) { Notes = [.. notes], Withdrawals = [.. withdrawals] };
    }

    private static bool TryParsePositive(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 1;
}
