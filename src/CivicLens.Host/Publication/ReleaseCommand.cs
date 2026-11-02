using System.Globalization;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;

namespace CivicLens.Host.Publication;

public static class ReleaseCommand
{
    private const int DefaultListLimit = 20;
    private const string WithdrawOption = "--withdraw";
    private const string RewriteOfficialsOption = "--rewrite-officials";
    private const string RewriteEntriesOption = "--rewrite-entries";
    private const string RewritePagesOption = "--rewrite-pages";

    public static bool Matches(string[] args) => args is ["releases", ..];

    public static Task<int> ExecuteAsync(string[] args, PublishRelease publish,
        ListPublicationReleases list, ListBlockedPublished blocked, ActivatePublicationRelease activate, ReviewActor owner,
        TextWriter output, TextWriter error, CancellationToken cancellationToken) =>
        OwnerCommand.RunAsync(async () =>
        {
            try
            {
                switch (args)
                {
                    case ["releases", "publish", var key, .. var arguments]:
                        CommandOutput.Write(output, await publish.ExecuteAsync(owner, ParsePublish(key, arguments), cancellationToken));
                        return 0;
                    case ["releases", "list"]:
                        CommandOutput.Write(output, await list.ExecuteAsync(owner, DefaultListLimit, cancellationToken));
                        return 0;
                    case ["releases", "list", var limitText] when TryParsePositive(limitText, out var limit):
                        CommandOutput.Write(output, await list.ExecuteAsync(owner, limit, cancellationToken));
                        return 0;
                    case ["releases", "blocked"]:
                        CommandOutput.Write(output, await blocked.ExecuteAsync(owner, cancellationToken));
                        return 0;
                    case ["releases", "activate", var numberText] when TryParsePositive(numberText, out var number):
                        CommandOutput.Write(output, await activate.ExecuteAsync(owner, number, cancellationToken));
                        return 0;
                    default:
                        error.WriteLine($"Use releases publish <idempotency-key> [<kind>:<id>...] [--withdraw <kind>:<id> <text>]... [--rewrite-officials] [--rewrite-entries] [--rewrite-pages] (kind is one of: {PublishedRecordReference.KindNames}), releases list [limit], releases blocked, or releases activate <release-number>.");
                        return 2;
                }
            }
            catch (Exception exception) when (exception is PublicationConflictException or PublicationStateMismatchException or ReleaseRewriteException)
            {
                error.WriteLine(exception.Message);
                return 1;
            }
        }, error);

    private static PublishReleaseRequest ParsePublish(string key, string[] arguments)
    {
        List<PublishedRecordReference> records = [];
        List<RecordNote> withdrawals = [];
        var rewriteOfficials = false;
        var rewriteEntries = false;
        var rewritePages = false;
        for (var index = 0; index < arguments.Length; index++)
        {
            var option = arguments[index];
            if (option == RewriteOfficialsOption)
            {
                rewriteOfficials = true;
                continue;
            }
            if (option == RewriteEntriesOption)
            {
                rewriteEntries = true;
                continue;
            }
            if (option == RewritePagesOption)
            {
                rewritePages = true;
                continue;
            }
            if (option != WithdrawOption)
            {
                records.Add(PublishedRecordReference.Parse(option));
                continue;
            }
            if (index + 2 >= arguments.Length)
                throw new ArgumentException($"Give {option} as {option} <kind>:<id> <text>.");
            var reference = PublishedRecordReference.Parse(arguments[index + 1]);
            withdrawals.Add(new(reference.Kind, reference.RecordId, arguments[index + 2]));
            index += 2;
        }
        return new PublishReleaseRequest([.. records], key) { Withdrawals = [.. withdrawals], RewriteOfficials = rewriteOfficials, RewriteEntries = rewriteEntries, RewritePages = rewritePages };
    }

    private static bool TryParsePositive(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 1;
}
