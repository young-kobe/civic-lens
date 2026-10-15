using CivicLens.Application.Analysis;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Statements;

namespace CivicLens.Tests.Infrastructure.Analysis;

public sealed class PostgresAnalysisWakeupArgumentTests
{
    private const string Database = "Host=127.0.0.1;Port=1;Database=fixture;Username=fixture;Password=secret-sentinel;Timeout=1";

    [Theory]
    [InlineData("")]
    [InlineData("document_change_analysis; DROP TABLE x")]
    [InlineData("Statement_Analysis")]
    public void AQueueTableNameWithAnythingButLowercaseLettersAndUnderscoresIsRefused(string table) =>
        Assert.ThrowsAny<ArgumentException>(() => PostgresAnalysisWakeup.FromConnectionString(Database, table,
            DocumentChangeDraftTask.Task));

    [Theory]
    [InlineData("")]
    [InlineData("gate' OR '1'='1")]
    [InlineData("Gate")]
    [InlineData("statement_draft")]
    public void AQueueTaskWithAnythingButLowercaseLettersDigitsAndHyphensIsRefused(string task) =>
        Assert.ThrowsAny<ArgumentException>(() => PostgresAnalysisWakeup.FromConnectionString(Database,
            PostgresStatementAnalysisStore.Table, task));
}
