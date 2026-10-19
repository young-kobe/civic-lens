namespace CivicLens.Application.Review;

/// <summary>The result of the checks that Approve runs, kept as data so a list can show it before anyone decides.</summary>
public sealed record ApprovalCheck(string? Failure)
{
    public bool Passed => Failure is null;

    public static ApprovalCheck Of(Action check)
    {
        try
        {
            check();
            return new(Failure: null);
        }
        catch (ArgumentException exception)
        {
            return new(exception.Message);
        }
    }

    public static async Task<ApprovalCheck> OfAsync(Func<Task> check)
    {
        try
        {
            await check();
            return new(Failure: null);
        }
        catch (ArgumentException exception)
        {
            return new(exception.Message);
        }
    }
}
