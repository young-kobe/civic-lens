namespace CivicLens.PublicApi.Bills;

/// <summary>One page of the bill list of the newest Congress. <c>Congress</c> is null when the release holds no bill. <c>BillsInCongress</c> is the stored total
/// of that Congress, or null when the release stores none.</summary>
public sealed record NewestCongressBills(BillPage Page, int? Congress, int? BillsInCongress);
