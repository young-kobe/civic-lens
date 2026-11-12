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
    public async Task RenewalFailureDoesNotCancelOwnershipBeforeTheLeaseDeadline()
    {
        using var ownership = new CancellationTokenSource();
        var started = Stopwatch.StartNew();

        await WorkerLoop.RenewLeaseAsync(_ => throw new InvalidOperationException("Database unavailable."), FailureTolerantLease, ownership)
            .WaitAsync(HangGuard);

        Assert.True(ownership.IsCancellationRequested);
        Assert.True(started.Elapsed >= FailureTolerantLease);
    }

    [Fact]
    public async Task ARefusedRenewalCancelsOwnershipAtOnce()
    {
        using var ownership = new CancellationTokenSource();
        var clock = new ManualClock();
        var calls = 0;
        var renewal = WorkerLoop.RenewLeaseAsync(_ => { calls++; return Task.FromResult(false); }, Lease, ownership, clock);

        await clock.AdvanceAsync(Lease / 3, timersCreated: 1).WaitAsync(HangGuard);
        await renewal.WaitAsync(HangGuard);

        Assert.True(ownership.IsCancellationRequested);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ARecoveredRenewalKeepsOwnership()
    {
        using var ownership = new CancellationTokenSource();
        var clock = new ManualClock();
        var calls = 0;
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renewal = WorkerLoop.RenewLeaseAsync(_ =>
        {
            calls++;
            if (calls == 1) throw new InvalidOperationException("Brief outage.");
            if (calls == 3) recovered.TrySetResult();
            return Task.FromResult(true);
        }, FailureTolerantLease, ownership, clock);
        var interval = FailureTolerantLease / 3;

        // The loop creates its timers in a fixed order: wait 1, wait 2 (after the failure), wait 3 (after the recovery).
        // The second renewal moves the deadline to the third interval, so the third renewal still runs at the old deadline.
        await clock.AdvanceAsync(interval, timersCreated: 1).WaitAsync(HangGuard);
        await clock.AdvanceAsync(interval, timersCreated: 3).WaitAsync(HangGuard);
        await clock.AdvanceAsync(interval, timersCreated: 5).WaitAsync(HangGuard);
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
        var clock = new ManualClock();
        var tick = TimeSpan.FromTicks(1);
        var renewal = WorkerLoop.RenewLeaseAsync(async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return true;
        }, Lease, ownership, clock);

        // Timer 1 is the wait before the renewal. Timer 2 is the attempt time limit, due at the lease deadline.
        await clock.AdvanceAsync(Lease / 3, timersCreated: 1).WaitAsync(HangGuard);
        await clock.AdvanceAsync(Lease - Lease / 3 - tick, timersCreated: 2).WaitAsync(HangGuard);
        Assert.False(ownership.IsCancellationRequested);

        await clock.AdvanceAsync(tick, timersCreated: 2).WaitAsync(HangGuard);
        await renewal.WaitAsync(HangGuard);

        Assert.True(ownership.IsCancellationRequested);
    }

    [Fact]
    public async Task ASlowSuccessfulRenewalExtendsTheLeaseFromWhenItWasSent()
    {
        using var ownership = new CancellationTokenSource();
        var clock = new ManualClock();
        var tick = TimeSpan.FromTicks(1);
        var interval = TimeSpan.FromTicks(SlowRenewalLease.Ticks / 3);
        var calls = 0;
        var renewal = WorkerLoop.RenewLeaseAsync(async token =>
        {
            if (++calls > 1) await Task.Delay(Timeout.Infinite, token);
            await Task.Delay(SlowRenewalLease / 2, clock, token);
            return true;
        }, SlowRenewalLease, ownership, clock);

        // Renewal 1 is sent at one interval and takes half a lease. Its timers are 1 (wait), 2 (time limit), 3 (work).
        await clock.AdvanceAsync(interval, timersCreated: 1).WaitAsync(HangGuard);
        await clock.AdvanceAsync(SlowRenewalLease / 2, timersCreated: 3).WaitAsync(HangGuard);
        // The new deadline is the send time plus one lease. Timer 4 is the wait before renewal 2, timer 5 its time limit.
        await clock.AdvanceAsync(interval, timersCreated: 4).WaitAsync(HangGuard);
        var deadline = interval + SlowRenewalLease;
        var untilDeadline = deadline - (interval + SlowRenewalLease / 2 + interval);
        await clock.AdvanceAsync(untilDeadline - tick, timersCreated: 5).WaitAsync(HangGuard);
        Assert.False(ownership.IsCancellationRequested);

        await clock.AdvanceAsync(tick, timersCreated: 5).WaitAsync(HangGuard);
        await renewal.WaitAsync(HangGuard);

        Assert.True(ownership.IsCancellationRequested);
        Assert.Equal(2, calls);
    }

    private sealed class ManualClock : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ManualTimer> timers = [];
        private TaskCompletionSource created = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int timersCreated;
        private TimeSpan now;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            lock (gate) return now.Ticks;
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            lock (gate)
            {
                timer.Schedule(now, dueTime);
                timers.Add(timer);
                timersCreated++;
                created.TrySetResult();
                created = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            return timer;
        }

        public async Task AdvanceAsync(TimeSpan span, int timersCreated)
        {
            while (true)
            {
                Task changed;
                lock (gate)
                {
                    if (this.timersCreated >= timersCreated) break;
                    changed = created.Task;
                }
                await changed;
            }
            List<ManualTimer> due;
            lock (gate)
            {
                now += span;
                due = timers.Where(timer => timer.IsDue(now)).ToList();
                foreach (var timer in due) timer.Fire();
            }
            foreach (var timer in due) timer.Callback();
        }

        private void Remove(ManualTimer timer)
        {
            lock (gate) timers.Remove(timer);
        }

        private sealed class ManualTimer(ManualClock owner, TimerCallback callback, object? state) : ITimer
        {
            private TimeSpan? dueAt;

            public void Schedule(TimeSpan now, TimeSpan dueTime) =>
                dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : now + dueTime;

            public bool IsDue(TimeSpan now) => dueAt <= now;

            public void Fire() => dueAt = null;

            public void Callback() => callback(state);

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner.gate) Schedule(owner.now, dueTime);
                return true;
            }

            public void Dispose() => owner.Remove(this);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
