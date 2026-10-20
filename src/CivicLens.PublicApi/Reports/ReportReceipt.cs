namespace CivicLens.PublicApi.Reports;

/// <summary>The result of a store attempt. A duplicate report is <see cref="Stored"/>, so a retry looks the same as the first send.</summary>
public enum ReportReceipt { Stored, NoRelease, UnknownRecord, Closed }
