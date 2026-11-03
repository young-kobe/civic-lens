using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Publication;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresRecordIdCheckTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    public static TheoryData<string> Samples() =>
    [
        new string('a', 32),
        new string('b', 64),
        new string('A', 32),
        new string('A', 64),
        new string('c', 48),
        new string('d', 65),
        new string('e', 31),
        new string('g', 32),
        new string('1', 32) + "\n",
        new string('1', 64) + "\n",
        ""
    ];

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task TheDatabaseCheckAcceptsExactlyTheIdsThatTheProtocolAccepts(string recordId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO published_records (kind, record_id, first_published_at_utc_ticks) VALUES ('action', @id, 1)", connection);
        command.Parameters.AddWithValue("id", recordId);

        var accepted = await InsertAcceptedAsync(command);

        Assert.Equal(PublicationProtocol.IsAnyRecordId(recordId), accepted);
    }

    private static async Task<bool> InsertAcceptedAsync(NpgsqlCommand command)
    {
        try
        {
            await command.ExecuteNonQueryAsync();
            return true;
        }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.CheckViolation or PostgresErrorCodes.StringDataRightTruncation)
        {
            return false;
        }
    }
}
