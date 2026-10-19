using System.Collections.Immutable;
using CivicLens.Application.Review;
using Microsoft.AspNetCore.Components;

namespace CivicLens.Host.Review;

public abstract class BulkTriagePage : TriagePage
{
    protected const string BulkForm = "approve-bulk";

    private static readonly ReviewFailureMessages BulkFailures = new(
        "A record changed after you opened this page. The page shows the latest state.",
        $"Choose 1 to {BulkApproval.MaximumRecords} records that are safe to approve in bulk.",
        "The bulk approval could not be saved. Try again.");

    protected IReadOnlyList<BulkApprovalResult>? approvals;

    [SupplyParameterFromForm(FormName = BulkForm, Name = "targets")] private string[]? Targets { get; set; }
    [SupplyParameterFromForm(FormName = BulkForm, Name = "idempotencyKey")] private string? BulkKey { get; set; }

    protected abstract Task<ImmutableArray<BulkApprovalResult>> ApproveBulkAsync(BulkApprovalRequest request);

    protected Task ApproveSelectedAsync() => RunAsync(
        () => ApproveBulkAsync(new([.. (Targets ?? []).Select(TriageTargets.Parse)], $"bulk:{BulkKey}")), BulkFailures, results =>
        {
            approvals = results;
            return LoadAsync();
        });
}
