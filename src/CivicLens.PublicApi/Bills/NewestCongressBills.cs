namespace CivicLens.PublicApi.Bills;

/// <summary>One page of the bill list of the newest Congress. <c>Congress</c> is null when the release holds no bill.</summary>
public sealed record NewestCongressBills(BillPage Page, int? Congress);
