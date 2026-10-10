using System.Diagnostics;
using CivicLens.Application;

namespace CivicLens.Tests.Application;

public sealed class WorkerLoopTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan Slack = TimeSpan.FromMilliseconds(500);

    [Fact]
    public async Task RenewalToleratesFailuresUntilTheLeaseDeadline()
    {
        using var ownership = new CancellationTokenSource();
        var calls = 0;
        var started = Stopwatch.StartNew();

        await WorkerLoop.RenewLeaseAsync(_ => { calls++; throw new InvalidOperationException("Database unavailable."); }, Lease, ownership);

        Assert.True(ownership.IsCancellationRequested);
        Assert.True(calls >= 2, $"Only {calls} renewal attempts were made.");
        Assert.True(started.Elapsed >= Lease);
    }

    [Fact]
    public async Task ARefusedRenewalCancelsOwnershipAtOnce()
    {
        using var ownership = new CancellationTokenSource();
        var calls = 0;

        await WorkerLoop.RenewLeaseAsync(_ => { calls++; return Task.FromResult(false); }, Lease, ownership);

        Assert.True(ownership.IsCancellationRequested);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ARecoveredRenewalKeepsOwnership()
    {
        using var ownership = new CancellationTokenSource();
        var calls = 0;
        var renewal = WorkerLoop.RenewLeaseAsync(_ =>
        {
            calls++;
            return calls == 1 ? throw new InvalidOperationException("Brief outage.") : Task.FromResult(true);
        }, Lease, ownership);

        await Task.Delay(Lease * 2);

        Assert.False(ownership.IsCancellationRequested);
        await ownership.CancelAsync();
        await renewal;
    }

    [Fact]
    public async Task AHungRenewalCannotHoldOwnershipPastTheLeaseDeadline()
    {
        using var ownership = new CancellationTokenSource();
        var started = Stopwatch.StartNew();

        await WorkerLoop.RenewLeaseAsync(async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return true;
        }, Lease, ownership);

        Assert.True(ownership.IsCancellationRequested);
        Assert.InRange(started.Elapsed, Lease * 0.9, Lease + Slack);
    }

    [Fact]
    public async Task ASlowSuccessfulRenewalExtendsTheLeaseFromWhenItWasSent()
    {
        using var ownership = new CancellationTokenSource();
        var started = Stopwatch.StartNew();
        var first = true;

        await WorkerLoop.RenewLeaseAsync(async token =>
        {
            if (!first) await Task.Delay(Timeout.Infinite, token);
            first = false;
            await Task.Delay(Lease / 2, token);
            return true;
        }, Lease, ownership);

        var sentAt = Lease / 3;
        Assert.InRange(started.Elapsed, sentAt + Lease * 0.9, sentAt + Lease + Slack);
    }
}
