namespace CivicLens.Application.Analysis;

/// <summary>The ending of one record in an owner measure. Rejected means that code refused the model answer. A model stop, such as a cut answer, is Failed.
/// StoppedByCap and StoppedByModel end the whole run.</summary>
public enum AnalysisMeasureOutcome { Accepted, Rejected, Failed, Skipped, StoppedByCap, StoppedByModel }
