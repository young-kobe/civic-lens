using System.Globalization;
using CivicLens.Application.Spending;
using CivicLens.Core.Review;
using CivicLens.Host.Components.Layout;
using CivicLens.Host.Review;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;

namespace CivicLens.Host.Spending;

public sealed partial class AiSpendingPage
{
    private const string CapForm = "cap";
    private const string PriceForm = "price";

    private static readonly IReadOnlyList<Crumb> Crumbs = [new("Review", "/Review"), new("AI spending")];

    private static readonly ReviewFailureMessages Failures = new(
        "The change could not be saved. Reload the page and try again.",
        "Check each value. A cap is 0 to 1,000,000 dollars. A price is 0 to 1,000 dollars. Use at most 6 decimals.",
        "The change could not be saved. Your values are kept. Try again.");

    private ReviewActor? actor;
    private AiSpendingView? view;
    private string? error;

    [Inject] private ReviewActorAccessor Actors { get; set; } = default!;
    [Inject] private ILogger<AiSpendingPage> Logger { get; set; } = default!;
    [Inject] private GetAiSpending GetSpending { get; set; } = default!;
    [Inject] private SaveMonthlyCap SaveCap { get; set; } = default!;
    [Inject] private SaveModelPrice SavePrice { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [CascadingParameter] private HttpContext HttpContext { get; set; } = default!;
    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "status")] private string? Status { get; set; }

    [SupplyParameterFromForm(FormName = CapForm)] private CapInput? Cap { get; set; }
    [SupplyParameterFromForm(FormName = PriceForm)] private PriceInput? Price { get; set; }

    private string? StatusMessage => HttpMethods.IsPost(HttpContext.Request.Method) ? null : Status switch
    {
        CapForm => "The cap is saved.",
        PriceForm => "The price is saved.",
        _ => null
    };

    private IEnumerable<string> MissingPrices =>
        AiSpendingPolicy.ModelsInUse.Where(model => view!.CurrentPrices.All(current => current.Price.Model != model));

    private IReadOnlyList<ChangeLine> Changes => [.. view!.Caps
        .Select(cap => new ChangeLine($"Monthly cap set to {Dollars(cap.CapMicros)}", cap.AuthorSubject, cap.RecordedAt))
        .Concat(view.PriceChanges.Select(change => new ChangeLine(
            $"{change.Price.Model} priced at {Dollars(change.Price.InputMicros)} input, {Dollars(change.Price.OutputMicros)} output, " +
            $"{Dollars(change.Price.CacheReadMicros)} cache read, {Dollars(change.Price.CacheWriteMicros)} cache write",
            change.AuthorSubject, change.RecordedAt)))
        .OrderByDescending(line => line.RecordedAt).Take(GetAiSpending.ChangeLimit)];

    protected override async Task OnInitializedAsync()
    {
        actor = Actors.GetActor((await AuthenticationState).User);
        if (actor.Role != ReviewRole.Owner)
        {
            Navigation.NavigateTo(ReviewCommand.AccessDenied, forceLoad: true);
            return;
        }
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        try
        {
            view = await GetSpending.ExecuteAsync(actor!, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            Logger.LogError(exception, "The AI spending page could not load.");
            HttpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            error = "The AI spending page could not load. Refresh the page to try again.";
        }
    }

    private Task SaveCapAsync() => RunAsync(async () =>
    {
        await SaveCap.ExecuteAsync(actor!, ParseDollars(Cap?.Dollars), HttpContext.RequestAborted);
        return SpendingRoutes.Saved(CapForm);
    });

    private Task SavePriceAsync() => RunAsync(async () =>
    {
        var input = Price ?? throw new ArgumentException("A price needs its values.");
        await SavePrice.ExecuteAsync(actor!, new SaveModelPriceRequest(input.Model ?? "", ParseDollars(input.Input),
            ParseDollars(input.Output), ParseDollars(input.CacheRead), ParseDollars(input.CacheWrite)), HttpContext.RequestAborted);
        return SpendingRoutes.Saved(PriceForm);
    });

    private async Task RunAsync(Func<Task<string>> save)
    {
        var (target, failure, _) = await ReviewCommand.RunAsync(save, HttpContext.Response, Logger, HttpContext.RequestAborted);
        error = failure switch
        {
            ReviewCommandFailure.Conflict => Failures.Conflict,
            ReviewCommandFailure.Invalid => Failures.Invalid,
            ReviewCommandFailure.Unavailable => Failures.Unavailable,
            _ => null
        };
        if (failure == ReviewCommandFailure.AccessDenied) Navigation.NavigateTo(ReviewCommand.AccessDenied);
        else if (target is not null) Navigation.NavigateTo(target);
    }

    private static decimal ParseDollars(string? text) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var dollars)
            ? dollars
            : throw new ArgumentException("A value must be a number of dollars.");

    private static string Dollars(long micros) =>
        "$" + AiSpendingPolicy.ToDollars(micros).ToString("0.00####", CultureInfo.InvariantCulture);

    private sealed record ChangeLine(string Text, string AuthorSubject, DateTimeOffset RecordedAt);

    public sealed class CapInput
    {
        public string? Dollars { get; set; }
    }

    public sealed class PriceInput
    {
        public string? Model { get; set; }
        public string? Input { get; set; }
        public string? Output { get; set; }
        public string? CacheRead { get; set; }
        public string? CacheWrite { get; set; }
    }
}
