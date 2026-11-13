namespace CivicLens.PublicApi.Statements;

/// <summary>The facts of one statement that the list shows beside its row. <c>SourceUrl</c> is the address of the source page.
/// <c>LinkedToVote</c> is true when an approved relationship links the statement to a vote.</summary>
public sealed record StatementListFacts(string SourceUrl, bool LinkedToVote);
