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

    public static bool IsPublish(string[] args) => args is ["releases", "publish", ..];

    public static Task<int> ExecuteAsync(string[] args, PublishRelease publish,
        ListPublicationReleases list, ListBlockedPublished blocked, ActivatePublicationRelease activate, BackfillFirstPublished backfill, BackfillPublishedEntries entries,
        BackfillStoredColumns columns, VerifyPublishedEntries verify, VerifyCorrectionHistories verifyCorrections,
        BackfillCorrectionHistories corrections, BackfillReleaseBillCounts billCounts, VerifyReleaseBillCounts verifyBillCounts,
        BackfillReleasePartyLineUps lineUps, VerifyReleasePartyLineUps verifyLineUps, VerifyVotePartyTallies verifyTallies,
        ReviewActor owner, TextWriter output, TextWriter error, CancellationToken cancellationToken) =>
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
                    case ["releases", "first-published", "backfill"]:
                        CommandOutput.Write(output, await backfill.ExecuteAsync(owner, cancellationToken));
                        return 0;
                    case ["releases", "entries", "backfill", var entriesLimit] when TryParsePositive(entriesLimit, out var entriesCount):
                        var result = await entries.ExecuteAsync(owner, entriesCount, cancellationToken);
                        CommandOutput.Write(output, result);
                        return result.Skipped == 0 && result.OpenRangesWithoutRow == 0 && result.ReadyRangesWithoutRow == 0 && result.OpenStatementsWithoutApprover == 0 ? 0 : 1;
                    case ["releases", "corrections", "backfill", var correctionLimit] when TryParsePositive(correctionLimit, out var correctionCount):
                        var resultWithoutCursor = await corrections
                            .ExecuteAsync(owner, correctionCount, null, cancellationToken);
                        CommandOutput.Write(output, resultWithoutCursor);
                        return resultWithoutCursor.Failed == 0 ? 0 : 1;
                    case ["releases", "corrections", "backfill", var correctionLimitWithCursor, var cursorText] when TryParsePositive(correctionLimitWithCursor, out var correctionCountWithCursor):
                        var cursor = CorrectionHistoryBootstrapCursor.Parse(cursorText);
                        var correctionResult = await corrections
                            .ExecuteAsync(owner, correctionCountWithCursor, cursor, cancellationToken);
                        CommandOutput.Write(output, correctionResult);
                        return correctionResult.Failed == 0 ? 0 : 1;
                    case ["releases", "corrections", "verify", var verifyLimit] when TryParsePositive(verifyLimit, out var verifyCount):
                        var verification = await verifyCorrections.ExecuteAsync(owner, verifyCount, null, cancellationToken);
                        CommandOutput.Write(output, verification);
                        return verification.Failed == 0 ? 0 : 1;
                    case ["releases", "corrections", "verify", var verifyLimitWithCursor, var verifyCursorText] when TryParsePositive(verifyLimitWithCursor, out var verifyCountWithCursor):
                        var verifyCursor = CorrectionHistoryVerificationCursor.Parse(verifyCursorText);
                        var continuedVerification = await verifyCorrections.ExecuteAsync(owner, verifyCountWithCursor, verifyCursor, cancellationToken);
                        CommandOutput.Write(output, continuedVerification);
                        return continuedVerification.Failed == 0 ? 0 : 1;
                    case ["releases", "counts", "backfill", var countLimit, .. var countCursor]
                        when TryParsePositive(countLimit, out var countTotal) && TryParseBackfillCursor(countCursor, out var countAfter):
                        var counted = await billCounts.ExecuteAsync(owner, countTotal, countAfter, cancellationToken);
                        CommandOutput.Write(output, counted);
                        return counted.Failed == 0 && !counted.StaleCursor ? 0 : 1;
                    case ["releases", "counts", "verify", var countVerifyLimit, .. var countVerifyCursor]
                        when TryParsePositive(countVerifyLimit, out var countVerifyTotal) && countVerifyCursor.Length <= 1:
                        var countVerification = await verifyBillCounts.ExecuteAsync(owner, countVerifyTotal,
                            ReleaseBillCountVerificationCursor.Parse(countVerifyCursor.FirstOrDefault()), cancellationToken);
                        CommandOutput.Write(output, countVerification);
                        return countVerification.Failed == 0 && !countVerification.StaleCursor ? 0 : 1;
                    case ["releases", "tallies", "verify", var tallyLimit, .. var tallyCursor]
                        when TryParsePositive(tallyLimit, out var tallyTotal) && tallyCursor.Length <= 1:
                        var tallyVerification = await verifyTallies.ExecuteAsync(owner, tallyTotal,
                            VotePartyTallyVerificationCursor.Parse(tallyCursor.FirstOrDefault()), cancellationToken);
                        CommandOutput.Write(output, tallyVerification);
                        return tallyVerification.Failed == 0 ? 0 : 1;
                    case ["releases", "lineups", "backfill", var lineUpLimit, .. var lineUpCursor]
                        when TryParsePositive(lineUpLimit, out var lineUpTotal) && TryParseBackfillCursor(lineUpCursor, out var lineUpAfter):
                        var lineUpResult = await lineUps.ExecuteAsync(owner, lineUpTotal, lineUpAfter, cancellationToken);
                        CommandOutput.Write(output, lineUpResult);
                        return lineUpResult.Failed == 0 && !lineUpResult.StaleCursor ? 0 : 1;
                    case ["releases", "lineups", "verify", var lineUpVerifyLimit, .. var lineUpVerifyCursor]
                        when TryParsePositive(lineUpVerifyLimit, out var lineUpVerifyTotal) && lineUpVerifyCursor.Length <= 1:
                        var lineUpVerification = await verifyLineUps.ExecuteAsync(owner, lineUpVerifyTotal,
                            ReleaseBillCountVerificationCursor.Parse(lineUpVerifyCursor.FirstOrDefault()), cancellationToken);
                        CommandOutput.Write(output, lineUpVerification);
                        return lineUpVerification.Failed == 0 && !lineUpVerification.StaleCursor ? 0 : 1;
                    case ["releases", "columns", "backfill"]:
                        var filled = await columns.ExecuteAsync(owner, cancellationToken);
                        CommandOutput.Write(output, filled);
                        return filled.OpenVoteRevisionsWithoutKeptFile == 0 ? 0 : 1;
                    case ["releases", "entries", "verify"]:
                        var publishedVerification = await verify.ExecuteAsync(owner, cancellationToken);
                        CommandOutput.Write(output, publishedVerification);
                        return publishedVerification.Failed == 0 ? 0 : 1;
                    default:
                        error.WriteLine($"Use releases publish <idempotency-key> [<kind>:<id>...] [--withdraw <kind>:<id> <text>]... [--rewrite-officials] [--rewrite-entries] [--rewrite-pages] (kind is one of: {PublishedRecordReference.KindNames}), releases list [limit], releases blocked, releases activate <release-number>, releases first-published backfill, releases entries backfill <limit>, releases corrections backfill <limit> [cursor], releases corrections verify <limit> [cursor], releases counts backfill <limit> [cursor], releases counts verify <limit> [cursor], releases lineups backfill <limit> [cursor], releases lineups verify <limit> [cursor], releases tallies verify <limit> [cursor], releases columns backfill, or releases entries verify.");
                        return 2;
                }
            }
            catch (Exception exception) when (exception is PublicationConflictException or PublicationStateMismatchException or ReleaseRewriteException or FirstPublishedMissingException)
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

    private static bool TryParseBackfillCursor(string[] arguments, out ReleaseBillCountVerificationCursor? cursor)
    {
        cursor = null;
        if (arguments is []) return true;
        if (arguments is not [var value]) return false;
        if (TryParsePositive(value, out _)) return true;
        try
        {
            cursor = ReleaseBillCountVerificationCursor.Parse(value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryParsePositive(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 1;
}
