namespace CivicLens.PublicApi.Statements;

public sealed record StatementPage(int Release, StatementRow[] Statements, string? NextCursor);
