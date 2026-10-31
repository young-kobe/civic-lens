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
            return new(MessageOf(exception));
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
            return new(MessageOf(exception));
        }
    }

    private static string MessageOf(ArgumentException exception)
    {
        var suffix = $"(Parameter '{exception.ParamName}')";
        return exception.ParamName is not null && exception.Message.EndsWith(suffix, StringComparison.Ordinal)
            ? exception.Message[..^suffix.Length].TrimEnd()
            : exception.Message;
    }
}
