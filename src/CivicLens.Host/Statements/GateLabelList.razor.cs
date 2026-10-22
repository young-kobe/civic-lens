using CivicLens.Application.Statements;
using CivicLens.Core.Review;
using CivicLens.Host.Components.Layout;
using CivicLens.Host.Review;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;

namespace CivicLens.Host.Statements;

public sealed partial class GateLabelList
{
    private const int PageSize = 25;

    private static readonly IReadOnlyList<Crumb> Crumbs = [new("Review", "/Review"), new("Gate labels")];

    private GateLabelQueue? queue;
    private string? error;

    [Inject] private ReviewActorAccessor Actors { get; set; } = default!;
    [Inject] private ILogger<GateLabelList> Logger { get; set; } = default!;
    [Inject] private ListGateLabelQueue ListQueue { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [CascadingParameter] private HttpContext HttpContext { get; set; } = default!;
    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "status")] private string? Status { get; set; }

    private string? StatusMessage => Status == "done" ? "Every release has a label." : null;

    protected override async Task OnInitializedAsync()
    {
        var actor = Actors.GetActor((await AuthenticationState).User);
        if (actor.Role != ReviewRole.Owner)
        {
            Navigation.NavigateTo(GateLabelRoutes.AccessDenied, forceLoad: true);
            return;
        }
        var cancellationToken = HttpContext.RequestAborted;
        try
        {
            queue = await ListQueue.ExecuteAsync(actor, PageSize, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            Logger.LogError(exception, "The gate label list could not load.");
            error = "The label list could not load. Refresh the page to try again.";
        }
    }
}
