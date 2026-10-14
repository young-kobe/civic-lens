using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CivicLens.Application.Actions;
using CivicLens.Core.Review;
using CivicLens.Host.Statements;

namespace CivicLens.Host.Actions;

internal sealed class ActionCommand(StatementQueueCommands<ActionDraftRun, ActionDraftView, ActionDraftListItem> drafts, ReviewActor owner)
{
    private const string Usage = "Use actions draft status, actions draft list [drafted|unchanged|superseded|no-official|pending|failed] [limit], " +
        "or actions draft requeue <candidate-id|--all>.";
    private const int DefaultListLimit = 100;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static bool Matches(string[] args) => args is ["actions", ..];

    public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            switch (args)
            {
                case ["actions", "draft", "status"]:
                    Write(await drafts.Status.ExecuteAsync(owner, cancellationToken));
                    return 0;
                case ["actions", "draft", "list"]:
                    Write(await drafts.List.ExecuteAsync(owner, null, DefaultListLimit, cancellationToken));
                    return 0;
                case ["actions", "draft", "list", var view]:
                    Write(await drafts.List.ExecuteAsync(owner, ParseView(view), DefaultListLimit, cancellationToken));
                    return 0;
                case ["actions", "draft", "list", var view, var limit]:
                    Write(await drafts.List.ExecuteAsync(owner, ParseView(view), ParseLimit(limit), cancellationToken));
                    return 0;
                case ["actions", "draft", "requeue", "--all"]:
                    Write(new { requeued = await drafts.Requeue.ExecuteAsync(owner, null, cancellationToken) });
                    return 0;
                case ["actions", "draft", "requeue", var candidateId]:
                    Write(new { requeued = await drafts.Requeue.ExecuteAsync(owner, candidateId, cancellationToken) });
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

    private static ActionDraftView ParseView(string text) => text switch
    {
        "drafted" => ActionDraftView.Drafted,
        "unchanged" => ActionDraftView.Unchanged,
        "superseded" => ActionDraftView.Superseded,
        "no-official" => ActionDraftView.NoTrackedOfficial,
        "pending" => ActionDraftView.Pending,
        "failed" => ActionDraftView.Failed,
        _ => throw new ArgumentException(Usage)
    };

    private static int ParseLimit(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) ? limit : throw new ArgumentException(Usage);

    private static void Write(object value) => Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
