namespace CivicLens.Application;

public interface IIntervalWakeup : IWorkerWakeup
{
    /// <summary>True before the first wait, and after a wait that passed a multiple of the interval on the database clock.</summary>
    bool IntervalElapsed { get; }
}
