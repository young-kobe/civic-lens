namespace CivicLens.Core.Documents;

/// <summary>A changed text span with exact UTF-16 offsets into its source extractions.</summary>
public sealed record DocumentComparisonHunk(int BeforeStart, int BeforeLength, int AfterStart, int AfterLength,
    string BeforeText, string AfterText, int BeforeContextStart, string BeforeContext,
    int AfterContextStart, string AfterContext, System.Collections.Immutable.ImmutableArray<DocumentWordEdit> WordEdits);
