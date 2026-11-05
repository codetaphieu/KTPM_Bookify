using System.Data;
using Bookify.Application.Abstractions.Data;
using Bookify.Application.Abstractions.Queue;
using Bookify.Application.Bookings.ProcessBookingQueue;
using Dapper;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Bookify.Infrastructure.Queue;

// Picks apartments that have pending bookings and processes them in parallel:
// one worker per apartment (sequential inside an apartment, parallel across apartments).
[DisallowConcurrentExecution]
internal sealed class ProcessBookingQueueJob : IJob
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ISqlConnectionFactory _sqlConnectionFactory;
    private readonly IBookingQueue _bookingQueue;
    private readonly QueueOptions _queueOptions;
    private readonly ILogger<ProcessBookingQueueJob> _logger;

    public ProcessBookingQueueJob(
        IServiceScopeFactory serviceScopeFactory,
        ISqlConnectionFactory sqlConnectionFactory,
        IBookingQueue bookingQueue,
        IOptions<QueueOptions> queueOptions,
        ILogger<ProcessBookingQueueJob> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _sqlConnectionFactory = sqlConnectionFactory;
        _bookingQueue = bookingQueue;
        _queueOptions = queueOptions.Value;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        IReadOnlyList<Guid> apartmentIds = await _bookingQueue.GetPendingApartmentIdsAsync(
            _queueOptions.BatchSize,
            context.CancellationToken);

        if (apartmentIds.Count == 0)
        {
            return;
        }

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = _queueOptions.MaxDegreeOfParallelism,
            CancellationToken = context.CancellationToken
        };

        await Parallel.ForEachAsync(apartmentIds, parallelOptions, ProcessApartmentAsync);
    }

    private async ValueTask ProcessApartmentAsync(Guid apartmentId, CancellationToken cancellationToken)
    {
        // Session advisory lock per apartment: even with several API instances,
        // only one worker processes a given apartment at a time.
        using IDbConnection lockConnection = _sqlConnectionFactory.CreateConnection();

        bool locked = await lockConnection.ExecuteScalarAsync<bool>(
            "SELECT pg_try_advisory_lock(hashtext(@Key))",
            new { Key = apartmentId.ToString() });

        if (!locked)
        {
            return;
        }

        try
        {
            // Own scope => own DbContext, required for parallel processing.
            using IServiceScope scope = _serviceScopeFactory.CreateScope();
            ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();

            await sender.Send(new ProcessBookingQueueCommand(apartmentId), cancellationToken);
        }
        catch (Exception exception)
        {
            // Pending items stay in the queue and are retried on the next run.
            _logger.LogError(exception, "Error processing booking queue for apartment {ApartmentId}", apartmentId);
        }
        finally
        {
            await lockConnection.ExecuteAsync(
                "SELECT pg_advisory_unlock(hashtext(@Key))",
                new { Key = apartmentId.ToString() });
        }
    }
}
