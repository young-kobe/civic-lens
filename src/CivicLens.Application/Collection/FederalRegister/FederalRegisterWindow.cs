namespace CivicLens.Application.Collection.FederalRegister;

/// <summary>A window of publication dates. The next window starts a short time before the newest document that the store knows.</summary>
public readonly record struct FederalRegisterWindow(DateOnly First, DateOnly Last)
{
    public const int LookbackDays = 2;

    public static FederalRegisterWindow? Next(DateOnly? newestPublishedOn, DateOnly backfillFrom, DateOnly today)
    {
        var first = newestPublishedOn is { } newest && newest.AddDays(-LookbackDays) > backfillFrom ? newest.AddDays(-LookbackDays) : backfillFrom;
        return first > today ? null : new FederalRegisterWindow(first, today);
    }
}
