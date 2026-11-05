using Microsoft.Extensions.Options;
using Quartz;

namespace Bookify.Infrastructure.Queue;

internal sealed class ProcessBookingQueueJobSetup : IConfigureOptions<QuartzOptions>
{
    private readonly QueueOptions _queueOptions;

    public ProcessBookingQueueJobSetup(IOptions<QueueOptions> queueOptions)
    {
        _queueOptions = queueOptions.Value;
    }

    public void Configure(QuartzOptions options)
    {
        const string jobName = nameof(ProcessBookingQueueJob);

        options
            .AddJob<ProcessBookingQueueJob>(configure => configure.WithIdentity(jobName))
            .AddTrigger(configure =>
                configure
                    .ForJob(jobName)
                    .WithSimpleSchedule(schedule =>
                        schedule.WithIntervalInSeconds(_queueOptions.IntervalInSeconds).RepeatForever()));
    }
}
