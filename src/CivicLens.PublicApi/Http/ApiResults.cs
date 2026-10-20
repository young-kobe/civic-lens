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
        Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "no release is indexed yet");

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
