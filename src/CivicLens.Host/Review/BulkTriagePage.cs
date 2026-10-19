using System.Collections.Immutable;
using CivicLens.Application.Review;
using Microsoft.AspNetCore.Components;

namespace CivicLens.Host.Review;

public abstract class BulkTriagePage : TriagePage
{
    protected const string BulkForm = "approve-bulk";

    protected IReadOnlyList<BulkApprovalResult>? approvals;

    [SupplyParameterFromForm(FormName = BulkForm, Name = "targets")] private string[]? Targets { get; set; }
    [SupplyParameterFromForm(FormName = BulkForm, Name = "idempotencyKey")] private string? BulkKey { get; set; }

    protected abstract Task<ImmutableArray<BulkApprovalResult>> ApproveBulkAsync(BulkApprovalRequest request);

    protected async Task ApproveSelectedAsync()
    {
        try
        {
            approvals = await ApproveBulkAsync(new([.. (Targets ?? []).Select(TriageTargets.Parse)], BulkKey ?? ""));
        }
        catch (OperationCanceledException) when (RequestAborted.IsCancellationRequested) { throw; }
        catch (ArgumentException)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            error = $"Choose 1 to {BulkApproval.MaximumRecords} records that are safe to approve in bulk.";
        }
        await LoadAsync();
    }
}
