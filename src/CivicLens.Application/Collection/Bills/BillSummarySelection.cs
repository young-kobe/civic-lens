using System.Globalization;

namespace CivicLens.Application.Collection.Bills;

public static class BillSummarySelection
{
    public static CongressBillSummaryText? Newest(IEnumerable<CongressBillSummaryText> summaries)
    {
        ArgumentNullException.ThrowIfNull(summaries);
        return summaries.OrderByDescending(item => item.Summary.ActionDate).ThenByDescending(item => item.Summary.UpdateDate)
            .ThenByDescending(item => item.Summary.VersionCode, StringComparer.Ordinal).FirstOrDefault();
    }

    public static string VersionOf(CongressBillSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return $"{summary.VersionCode}@{summary.UpdateDate.UtcDateTime.ToString(CongressBillRenderer.UpdateFormat, CultureInfo.InvariantCulture)}";
    }
}
