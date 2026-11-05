namespace Bookify.Infrastructure.Queue;

public sealed class QueueOptions
{
    public int IntervalInSeconds { get; init; } = 1;

    // How many apartments are picked per run.
    public int BatchSize { get; init; } = 20;

    // How many apartments are processed in parallel (one worker per apartment).
    public int MaxDegreeOfParallelism { get; init; } = 4;
}
