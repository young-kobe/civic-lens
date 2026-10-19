using System.Globalization;
using CivicLens.Application.Statements;
using CivicLens.Core.Review;
using CivicLens.Host.Analysis;

namespace CivicLens.Host.Statements;

internal sealed class StatementCommand(AnalysisQueueCommands<StatementGateRun, StatementGateView, StatementGateListItem> gates,
    AnalysisQueueCommands<StatementDraftRun, StatementDraftView, StatementDraftListItem> drafts, RegateStatementCandidates regate,
    ExportGateLabels exportLabels, ReviewActor owner, string? currentIssueRevisionId, string runtimeDirectory)
{
    private const string Usage = "Use statements status, statements list [passed|skipped|failed-open|over-cap|pending|failed] [limit], " +
        "statements run <run-id>, statements requeue <candidate-id|--all>, statements regate [--since yyyy-MM-dd], " +
        "statements draft status, statements draft list [drafted|over-cap|existing|pending|failed] [limit], " +
        "statements draft run <run-id>, statements draft requeue <candidate-id|--all>, or statements labels export <path>, " +
        "where the path is inside .runtime.";
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
                    CommandOutput.Write(await gates.List.ExecuteAsync(owner, ParseView(view), OwnerCommand.ParseLimit(limit, Usage), cancellationToken));
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
                    CommandOutput.Write(await drafts.List.ExecuteAsync(owner, ParseDraftView(view), OwnerCommand.ParseLimit(limit, Usage), cancellationToken));
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
                case ["statements", "labels", "export", var path]:
                    return await ExportLabelsAsync(path, cancellationToken);
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        });

    private async Task<int> ExportLabelsAsync(string path, CancellationToken cancellationToken)
    {
        var target = GateLabelExport.ResolveTarget(path, runtimeDirectory);
        var rows = await exportLabels.ExecuteAsync(owner, cancellationToken);
        await GateLabelExport.WriteAsync(rows, target, cancellationToken);
        CommandOutput.Write(new { exported = rows.Count });
        return 0;
    }

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

    private static DateOnly ParseDate(string text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : throw new ArgumentException("The --since date must use yyyy-MM-dd.");
}
