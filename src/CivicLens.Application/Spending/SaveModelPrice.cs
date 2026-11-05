using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Spending;

public sealed record SaveModelPriceRequest(string Model, decimal InputDollars, decimal OutputDollars, decimal CacheReadDollars,
    decimal CacheWriteDollars);

public sealed class SaveModelPrice(IAiSpendingStore store)
{
    private const decimal MaximumDollars = ModelPrice.MaximumMicros / 1_000_000m;

    public Task ExecuteAsync(ReviewActor actor, SaveModelPriceRequest request, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentNullException.ThrowIfNull(request);
        if (!AiSpendingPolicy.ModelsInUse.Contains(request.Model))
            throw new ArgumentException("The model is not one that a stage uses.", nameof(request));
        var price = new ModelPrice(request.Model, Micros(request.InputDollars), Micros(request.OutputDollars),
            Micros(request.CacheReadDollars), Micros(request.CacheWriteDollars));
        return store.SavePriceAsync(actor.Subject, price, cancellationToken);
    }

    private static long Micros(decimal dollars) => AiSpendingPolicy.ToMicros(dollars, MaximumDollars, "price");
}
