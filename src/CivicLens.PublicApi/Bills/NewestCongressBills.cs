namespace CivicLens.PublicApi.Bills;

/// <summary>One page of the bill list of the newest Congress. <c>Congress</c> is null when the release holds no bill.
/// <c>BillsInCongress</c> counts the bills of that Congress in the release. No filter changes it.</summary>
public sealed record NewestCongressBills(BillPage Page, int? Congress, int BillsInCongress);
