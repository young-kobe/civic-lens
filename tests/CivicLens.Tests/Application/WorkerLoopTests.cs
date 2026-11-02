using System.Diagnostics;
using CivicLens.Application;

namespace CivicLens.Tests.Application;

public sealed class WorkerLoopTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan FailureTolerantLease = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SlowRenewalLease = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task RenewalToleratesFailuresUntilTheLeaseDeadline()
    {
        using var ownership = new CancellationTokenSource();
        var calls = 0;
        var started = Stopwatch.StartNew();

        await WorkerLoop.RenewLeaseAsync(_ => { calls++; throw new InvalidOperationException("Database unavailable."); }, FailureTolerantLease, ownership);

        Assert.True(ownership.IsCancellationRequested);
        Assert.True(calls >= 2, $"Only {calls} renewal attempts were made.");
        Assert.True(started.Elapsed >= FailureTolerantLease);
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
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renewal = WorkerLoop.RenewLeaseAsync(_ =>
        {
            calls++;
            if (calls == 1) throw new InvalidOperationException("Brief outage.");
            if (calls == 3) recovered.TrySetResult();
            return Task.FromResult(true);
        }, FailureTolerantLease, ownership);

        await Task.WhenAny(recovered.Task, renewal).WaitAsync(HangGuard);

        Assert.True(recovered.Task.IsCompleted);
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
        }, Lease, ownership).WaitAsync(HangGuard);

        Assert.True(ownership.IsCancellationRequested);
        Assert.InRange(started.Elapsed, Lease * 0.9, Lease + Lease / 3);
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
            await Task.Delay(SlowRenewalLease / 2, token);
            return true;
        }, SlowRenewalLease, ownership).WaitAsync(HangGuard);

        var sentAt = SlowRenewalLease / 3;
        Assert.InRange(started.Elapsed, sentAt + SlowRenewalLease * 0.9, sentAt + SlowRenewalLease + SlowRenewalLease / 4);
    }
}
