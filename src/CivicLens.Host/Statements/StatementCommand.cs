using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CivicLens.Application.Statements;
using CivicLens.Core.Review;

namespace CivicLens.Host.Statements;

internal sealed class StatementCommand(GetStatementGateStatus status, ListStatementGates list, GetStatementGateRun run,
    RequeueStatementGates requeue, RegateStatementCandidates regate, ReviewActor owner, string? currentIssueRevisionId)
{
    private const string Usage = "Use statements status, statements list [passed|skipped|failed-open|over-cap|pending|failed] [limit], " +
        "statements run <run-id>, statements requeue <candidate-id|--all>, or statements regate [--since yyyy-MM-dd].";
    private const int DefaultListLimit = 100;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static bool Matches(string[] args) => args is ["statements", ..];

    public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            switch (args)
            {
                case ["statements", "status"]:
                    Write(await status.ExecuteAsync(owner, cancellationToken));
                    return 0;
                case ["statements", "list"]:
                    Write(await list.ExecuteAsync(owner, null, DefaultListLimit, cancellationToken));
                    return 0;
                case ["statements", "list", var view]:
                    Write(await list.ExecuteAsync(owner, ParseView(view), DefaultListLimit, cancellationToken));
                    return 0;
                case ["statements", "list", var view, var limit]:
                    Write(await list.ExecuteAsync(owner, ParseView(view), ParseLimit(limit), cancellationToken));
                    return 0;
                case ["statements", "run", var runId]:
                    Write(await run.ExecuteAsync(owner, runId, cancellationToken)
                        ?? throw new ArgumentException("No gate run has that ID."));
                    return 0;
                case ["statements", "requeue", "--all"]:
                    Write(new { requeued = await requeue.ExecuteAsync(owner, null, cancellationToken) });
                    return 0;
                case ["statements", "requeue", var candidateId]:
                    Write(new { requeued = await requeue.ExecuteAsync(owner, candidateId, cancellationToken) });
                    return 0;
                case ["statements", "regate"]:
                    Write(new { requeued = await RegateAsync(null, cancellationToken) });
                    return 0;
                case ["statements", "regate", "--since", var since]:
                    Write(new { requeued = await RegateAsync(ParseDate(since), cancellationToken) });
                    return 0;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
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

    private static int ParseLimit(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) ? limit : throw new ArgumentException(Usage);

    private static DateOnly ParseDate(string text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : throw new ArgumentException("The --since date must use yyyy-MM-dd.");

    private static void Write(object value) => Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
