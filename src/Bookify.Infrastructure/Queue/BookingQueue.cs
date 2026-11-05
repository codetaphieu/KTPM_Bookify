using System.Data;
using Bookify.Application.Abstractions.Clock;
using Bookify.Application.Abstractions.Data;
using Bookify.Application.Abstractions.Queue;
using Bookify.Domain.Bookings;
using Dapper;

namespace Bookify.Infrastructure.Queue;

// PostgreSQL-table queue partitioned by apartment_id.
internal sealed class BookingQueue : IBookingQueue
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ISqlConnectionFactory _sqlConnectionFactory;
    private readonly IDateTimeProvider _dateTimeProvider;

    public BookingQueue(
        ApplicationDbContext dbContext,
        ISqlConnectionFactory sqlConnectionFactory,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _sqlConnectionFactory = sqlConnectionFactory;
        _dateTimeProvider = dateTimeProvider;
    }

    public void Enqueue(Guid bookingId, Guid apartmentId)
    {
        _dbContext.Add(new BookingQueueMessage(
            Guid.NewGuid(),
            bookingId,
            apartmentId,
            _dateTimeProvider.UtcNow));
    }

    public async Task<IReadOnlyList<Guid>> GetPendingApartmentIdsAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = _sqlConnectionFactory.CreateConnection();

        // Oldest waiting apartment first, so no apartment starves.
        const string sql = """
            SELECT apartment_id
            FROM booking_queue
            WHERE status = 'Pending'
            GROUP BY apartment_id
            ORDER BY MIN(created_on_utc)
            LIMIT @BatchSize
            """;

        IEnumerable<Guid> apartmentIds = await connection.QueryAsync<Guid>(
            new CommandDefinition(sql, new { BatchSize = batchSize }, cancellationToken: cancellationToken));

        return apartmentIds.ToList();
    }

    public async Task<IReadOnlyList<BookingQueueItem>> GetPendingItemsAsync(
        Guid apartmentId,
        CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = _sqlConnectionFactory.CreateConnection();

        const string sql = """
            SELECT
                id AS Id,
                booking_id AS BookingId,
                apartment_id AS ApartmentId,
                created_on_utc AS CreatedOnUtc
            FROM booking_queue
            WHERE apartment_id = @ApartmentId AND status = 'Pending'
            ORDER BY created_on_utc, id
            """;

        IEnumerable<BookingQueueItem> items = await connection.QueryAsync<BookingQueueItem>(
            new CommandDefinition(sql, new { ApartmentId = apartmentId }, cancellationToken: cancellationToken));

        return items.ToList();
    }

    public async Task MarkCompletedAsync(Guid queueItemId, CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = _sqlConnectionFactory.CreateConnection();

        const string sql = """
            UPDATE booking_queue
            SET status = @Status, processed_on_utc = @ProcessedOnUtc
            WHERE id = @Id
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = queueItemId, Status = BookingQueueMessage.Completed, ProcessedOnUtc = _dateTimeProvider.UtcNow },
            cancellationToken: cancellationToken));
    }

    public async Task MarkFailedAsync(Guid queueItemId, string error, CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = _sqlConnectionFactory.CreateConnection();
        using IDbTransaction transaction = connection.BeginTransaction();

        await MarkFailedAsync(connection, transaction, queueItemId, error, cancellationToken);

        transaction.Commit();
    }

    public async Task<bool> RecordFailedAttemptAsync(
        Guid queueItemId,
        string error,
        int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = _sqlConnectionFactory.CreateConnection();
        using IDbTransaction transaction = connection.BeginTransaction();

        const string sql = """
            UPDATE booking_queue
            SET attempts = attempts + 1, error = @Error
            WHERE id = @Id
            RETURNING attempts
            """;

        int attempts = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            new { Id = queueItemId, Error = error },
            transaction,
            cancellationToken: cancellationToken));

        bool deadLettered = attempts >= maxAttempts;

        if (deadLettered)
        {
            await MarkFailedAsync(connection, transaction, queueItemId, error, cancellationToken);
        }

        transaction.Commit();

        return deadLettered;
    }

    private async Task MarkFailedAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid queueItemId,
        string error,
        CancellationToken cancellationToken)
    {
        // The booking can no longer be processed, so it gets a final status instead of staying Processing forever.
        const string sql = """
            UPDATE booking_queue
            SET status = @Failed, processed_on_utc = @UtcNow, error = @Error
            WHERE id = @Id;

            UPDATE bookings
            SET status = @Rejected, rejected_on_utc = @UtcNow
            WHERE id = (SELECT booking_id FROM booking_queue WHERE id = @Id)
              AND status = @Processing;
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                Id = queueItemId,
                BookingQueueMessage.Failed,
                Error = error,
                _dateTimeProvider.UtcNow,
                Rejected = (int)BookingStatus.Rejected,
                Processing = (int)BookingStatus.Processing
            },
            transaction,
            cancellationToken: cancellationToken));
    }
}
