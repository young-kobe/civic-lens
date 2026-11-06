namespace CivicLens.Tests.Publication.Contracts;

public sealed class VoteWordingTableTests
{
    /// <summary>Set CIVIC_LENS_WORDING_TABLE to a file path and run this test to write the table that the owner approves.</summary>
    [Fact]
    public void TheWordingTableListsEveryDistinctSentenceOfBothTablesSoTheOwnerSeesWhatTheReaderSees()
    {
        var table = VoteWordingInputs.Table();

        Assert.Contains("| Passage | Passed |", table, StringComparison.Ordinal);
        if (Environment.GetEnvironmentVariable(VoteWordingInputs.TablePathVariable) is { Length: > 0 } path) File.WriteAllText(path, table);
    }
}
