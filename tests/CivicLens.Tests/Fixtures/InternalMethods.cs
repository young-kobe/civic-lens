using CivicLens.Infrastructure.Votes;

namespace CivicLens.Tests.Fixtures;

internal static class InternalMethods
{
    public static object Invoke(string typeName, string method, params object?[] arguments) =>
        typeof(PostgresVoteReviewStore).Assembly.GetType(typeName)!.GetMethod(method)!.Invoke(null, arguments)!;

    public static Task InvokeAsync(string typeName, string method, params object[] arguments) =>
        (Task)typeof(PostgresVoteReviewStore).Assembly.GetType(typeName)!.GetMethod(method)!.Invoke(null, arguments)!;
}
