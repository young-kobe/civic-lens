using System.Diagnostics;

namespace CivicLens.Application;

public static class WorkerLoop
{
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(2);

    public static bool IsRecoverable(Exception exception) =>
        exception is not OutOfMemoryException and not StackOverflowException;

    public static async Task<int> WaitForWorkAsync(IWorkerWakeup wakeup, bool reconnect, CancellationToken cancellationToken)
    {
        var failures = 0;
        if (!reconnect)
        {
            try
            {
                await wakeup.WaitAsync(cancellationToken);
                return failures;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                failures++;
            }
        }

        await Task.Delay(FailureBackoff, cancellationToken);
        return failures + await ConnectUntilAvailableAsync(wakeup, cancellationToken);
    }

    public static async Task<int> ConnectUntilAvailableAsync(IWorkerWakeup wakeup, CancellationToken cancellationToken)
    {
        var failures = 0;
        while (true)
        {
            try
            {
                await wakeup.ConnectAsync(cancellationToken);
                return failures;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                failures++;
                await Task.Delay(FailureBackoff, cancellationToken);
            }
        }
    }

    public static string ErrorCode(Exception exception) =>
        exception.GetType().Name is { Length: <= 128 } name ? name : "processingFailure";

    public static async Task RenewLeaseAsync(Func<CancellationToken, Task<bool>> renew, TimeSpan leaseDuration,
        CancellationTokenSource ownership)
    {
        var clock = Stopwatch.StartNew();
        var deadline = clock.Elapsed + leaseDuration;
        var interval = TimeSpan.FromTicks(leaseDuration.Ticks / 3);
        while (!ownership.IsCancellationRequested)
        {
            var wait = Min(interval, deadline - clock.Elapsed);
            if (wait <= TimeSpan.Zero) break;
            try { await Task.Delay(wait, ownership.Token); }
            catch (OperationCanceledException) when (ownership.IsCancellationRequested) { return; }
            var remaining = deadline - clock.Elapsed;
            if (remaining <= TimeSpan.Zero) break;
            var sent = clock.Elapsed;
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ownership.Token);
            attempt.CancelAfter(remaining);
            try
            {
                if (!await renew(attempt.Token)) break;
                deadline = sent + leaseDuration;
            }
            catch (OperationCanceledException) when (ownership.IsCancellationRequested) { return; }
            catch (Exception exception) when (IsRecoverable(exception)) { }
        }
        ownership.Cancel();
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;
}
