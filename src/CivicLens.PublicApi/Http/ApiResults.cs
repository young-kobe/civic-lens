using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace CivicLens.PublicApi.Http;

/// <summary>The answers that every endpoint of the service shares.</summary>
public static class ApiResults
{
    private const string StatementTimeoutState = "57014";

    public static IResult Json<T>(T value) => Results.Json(value, PublicationProtocol.JsonOptions);

    public static IResult NoRelease() =>
        Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "no release is active yet");

    public static IResult RecordNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "record not found");

    /// <summary>A cursor that names another release than the active one. The answer holds the active release number, so the caller starts the walk again.</summary>
    public static IResult ReleaseChanged(int activeRelease) => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "the release changed",
        detail: $"The cursor belongs to another release. Start again from the first page. The active release is {activeRelease}.",
        extensions: new Dictionary<string, object?> { ["release"] = activeRelease });

    public static async Task<IResult> GuardAsync(Func<Task<IResult>> read)
    {
        try
        {
            return await read();
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "invalid request", detail: exception.Message);
        }
        catch (PostgresException exception) when (exception.SqlState == StatementTimeoutState)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "the search took too long");
        }
    }
}
