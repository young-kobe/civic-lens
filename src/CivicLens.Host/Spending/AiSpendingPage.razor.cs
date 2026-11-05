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

    private static readonly IReadOnlyList<Crumb> Crumbs = [new("Review", "/Review"), new("AI spending")];

    private static readonly ReviewFailureMessages Failures = new(
        "The change could not be saved. Reload the page and try again.",
        "Check each value. A cap is 0 to 1,000,000 dollars. Use at most 6 decimals.",
        "The change could not be saved. Your values are kept. Try again.");

    private ReviewActor? actor;
    private AiSpendingView? view;
    private string? error;

    [Inject] private ReviewActorAccessor Actors { get; set; } = default!;
    [Inject] private ILogger<AiSpendingPage> Logger { get; set; } = default!;
    [Inject] private GetAiSpending GetSpending { get; set; } = default!;
    [Inject] private SaveMonthlyCap SaveCap { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [CascadingParameter] private HttpContext HttpContext { get; set; } = default!;
    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "status")] private string? Status { get; set; }

    [SupplyParameterFromForm(FormName = CapForm)] private CapInput? Cap { get; set; }

    private string? StatusMessage => !HttpMethods.IsPost(HttpContext.Request.Method) && Status == CapForm ? "The cap is saved." : null;

    private static readonly IReadOnlyList<PriceRow> PriceRows = [.. AiSpendingPolicy.ModelsInUse.Select(ModelPrices.For).SelectMany(PriceRowsOf)];

    private IReadOnlyList<ChangeLine> Changes => [.. view!.Caps
        .Select(cap => new ChangeLine($"Monthly cap set to {Dollars(cap.CapMicros)}", cap.AuthorSubject, cap.RecordedAt))];

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

    private static IEnumerable<PriceRow> PriceRowsOf(ModelPrice price) => price.Tiers.Select((tier, index) =>
        new PriceRow(price.Tiers.Count == 1 ? price.Model : $"{price.Model}, prompts {PromptRange(price.Tiers, index)}", tier));

    private static string PromptRange(IReadOnlyList<PriceTier> tiers, int index) => tiers[index].PromptTokensUpTo is { } limit
        ? $"up to {limit:N0} tokens"
        : $"over {tiers[index - 1].PromptTokensUpTo:N0} tokens";

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

    private sealed record PriceRow(string Name, PriceTier Tier);

    private sealed record ChangeLine(string Text, string AuthorSubject, DateTimeOffset RecordedAt);

    public sealed class CapInput
    {
        public string? Dollars { get; set; }
    }
}
