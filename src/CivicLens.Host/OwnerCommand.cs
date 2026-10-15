using CivicLens.Core.Review;

namespace CivicLens.Host;

internal static class OwnerCommand
{
    public static (string ConnectionString, ReviewActor Owner) ReadEnvironment()
    {
        var connectionString = Environment.GetEnvironmentVariable("CIVIC_LENS_DATABASE");
        var ownerSubject = Environment.GetEnvironmentVariable("CIVIC_LENS_REVIEW_OWNER");
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(ownerSubject))
            throw new ArgumentException("Owner commands require CIVIC_LENS_DATABASE and CIVIC_LENS_REVIEW_OWNER.");
        return (connectionString, new ReviewActor(ownerSubject, ReviewRole.Owner));
    }

    public static async Task<int> RunAsync(Func<Task<int>> body)
    {
        try
        {
            return await body();
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
}
