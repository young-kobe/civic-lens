namespace CivicLens.PublicApi.Officials;

/// <summary>The issues of an official that have the most sponsored bills and cast votes, in one Congress. <c>Scope</c> says what each count counts.</summary>
public sealed record IssueCountsFigures(int Congress, string Scope, IssueCountsFigure[] Issues);
