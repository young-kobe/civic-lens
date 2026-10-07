namespace CivicLens.Core.Documents;

/// <summary>A bounded changed word region. Zero length marks an insertion or deletion point.</summary>
public sealed record DocumentWordEdit(int BeforeStart, int BeforeLength, int AfterStart, int AfterLength);
