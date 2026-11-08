namespace CivicLens.Application.Collection.Bills;

public sealed record BillActionsBackfillPage(string JobId, int PageOrder, string ExtractionId, bool ClosesChain,
    int Congress, string BillType, int BillNumber, string SourceId);
