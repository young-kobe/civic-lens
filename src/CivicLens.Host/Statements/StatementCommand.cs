using System.Globalization;
using CivicLens.Application.Statements;
using CivicLens.Core.Review;

namespace CivicLens.Host.Statements;

internal sealed class StatementCommand(StatementQueueCommands<StatementGateRun, StatementGateView, StatementGateListItem> gates,
    StatementQueueCommands<StatementDraftRun, StatementDraftView, StatementDraftListItem> drafts, RegateStatementCandidates regate,
    ReviewActor owner, string? currentIssueRevisionId)
{
    private const string Usage = "Use statements status, statements list [passed|skipped|failed-open|over-cap|pending|failed] [limit], " +
        "statements run <run-id>, statements requeue <candidate-id|--all>, statements regate [--since yyyy-MM-dd], " +
        "statements draft status, statements draft list [drafted|over-cap|existing|pending|failed] [limit], " +
        "statements draft run <run-id>, or statements draft requeue <candidate-id|--all>.";
    private const int DefaultListLimit = 100;

    public static bool Matches(string[] args) => args is ["statements", ..];

    public Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken) =>
        OwnerCommand.RunAsync(async () =>
        {
            switch (args)
            {
                case ["statements", "status"]:
                    CommandOutput.Write(await gates.Status.ExecuteAsync(owner, cancellationToken));
                    return 0;
                case ["statements", "list"]:
                    CommandOutput.Write(await gates.List.ExecuteAsync(owner, null, DefaultListLimit, cancellationToken));
                    return 0;
                case ["statements", "list", var view]:
                    CommandOutput.Write(await gates.List.ExecuteAsync(owner, ParseView(view), DefaultListLimit, cancellationToken));
                    return 0;
                case ["statements", "list", var view, var limit]:
                    CommandOutput.Write(await gates.List.ExecuteAsync(owner, ParseView(view), ParseLimit(limit), cancellationToken));
                    return 0;
                case ["statements", "run", var runId]:
                    CommandOutput.Write(await gates.Run.ExecuteAsync(owner, runId, cancellationToken)
                        ?? throw new ArgumentException("No gate run has that ID."));
                    return 0;
                case ["statements", "requeue", "--all"]:
                    CommandOutput.Write(new { requeued = await gates.Requeue.ExecuteAsync(owner, null, cancellationToken) });
                    return 0;
                case ["statements", "requeue", var candidateId]:
                    CommandOutput.Write(new { requeued = await gates.Requeue.ExecuteAsync(owner, candidateId, cancellationToken) });
                    return 0;
                case ["statements", "regate"]:
                    CommandOutput.Write(new { requeued = await RegateAsync(null, cancellationToken) });
                    return 0;
                case ["statements", "regate", "--since", var since]:
                    CommandOutput.Write(new { requeued = await RegateAsync(ParseDate(since), cancellationToken) });
                    return 0;
                case ["statements", "draft", "status"]:
                    CommandOutput.Write(await drafts.Status.ExecuteAsync(owner, cancellationToken));
                    return 0;
                case ["statements", "draft", "list"]:
                    CommandOutput.Write(await drafts.List.ExecuteAsync(owner, null, DefaultListLimit, cancellationToken));
                    return 0;
                case ["statements", "draft", "list", var view]:
                    CommandOutput.Write(await drafts.List.ExecuteAsync(owner, ParseDraftView(view), DefaultListLimit, cancellationToken));
                    return 0;
                case ["statements", "draft", "list", var view, var limit]:
                    CommandOutput.Write(await drafts.List.ExecuteAsync(owner, ParseDraftView(view), ParseLimit(limit), cancellationToken));
                    return 0;
                case ["statements", "draft", "run", var runId]:
                    CommandOutput.Write(await drafts.Run.ExecuteAsync(owner, runId, cancellationToken)
                        ?? throw new ArgumentException("No draft run has that ID."));
                    return 0;
                case ["statements", "draft", "requeue", "--all"]:
                    CommandOutput.Write(new { requeued = await drafts.Requeue.ExecuteAsync(owner, null, cancellationToken) });
                    return 0;
                case ["statements", "draft", "requeue", var candidateId]:
                    CommandOutput.Write(new { requeued = await drafts.Requeue.ExecuteAsync(owner, candidateId, cancellationToken) });
                    return 0;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        });

    private Task<int> RegateAsync(DateOnly? since, CancellationToken cancellationToken) =>
        regate.ExecuteAsync(owner, currentIssueRevisionId
            ?? throw new ArgumentException("Regating needs CIVIC_LENS_ISSUES_CONFIG to name the current issue configuration."),
            since, cancellationToken);

    private static StatementGateView ParseView(string text) => text switch
    {
        "passed" => StatementGateView.Passed,
        "skipped" => StatementGateView.Skipped,
        "failed-open" => StatementGateView.FailedOpen,
        "over-cap" => StatementGateView.OverCap,
        "pending" => StatementGateView.Pending,
        "failed" => StatementGateView.Failed,
        _ => throw new ArgumentException(Usage)
    };

    private static StatementDraftView ParseDraftView(string text) => text switch
    {
        "drafted" => StatementDraftView.Drafted,
        "over-cap" => StatementDraftView.OverCap,
        "existing" => StatementDraftView.Existing,
        "pending" => StatementDraftView.Pending,
        "failed" => StatementDraftView.Failed,
        _ => throw new ArgumentException(Usage)
    };

    private static int ParseLimit(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) ? limit : throw new ArgumentException(Usage);

    private static DateOnly ParseDate(string text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : throw new ArgumentException("The --since date must use yyyy-MM-dd.");
}
