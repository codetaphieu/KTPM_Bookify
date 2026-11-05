namespace Bookify.Application.Abstractions.Queue;

public sealed record BookingQueueItem(
    Guid Id,
    Guid BookingId,
    Guid ApartmentId,
    DateTime CreatedOnUtc);

public interface IBookingQueue
{
    // Persisted together with the booking by IUnitOfWork.SaveChangesAsync (same transaction).
    void Enqueue(Guid bookingId, Guid apartmentId);

    Task<IReadOnlyList<Guid>> GetPendingApartmentIdsAsync(int batchSize, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BookingQueueItem>> GetPendingItemsAsync(Guid apartmentId, CancellationToken cancellationToken = default);

    Task MarkCompletedAsync(Guid queueItemId, CancellationToken cancellationToken = default);

    // Gives up on the item: marks it Failed and rejects its booking if it is still Processing,
    // so the client polling the booking always gets a final result.
    Task MarkFailedAsync(Guid queueItemId, string error, CancellationToken cancellationToken = default);

    // Records a failed attempt. Returns true when maxAttempts is reached and the item was marked Failed.
    Task<bool> RecordFailedAttemptAsync(
        Guid queueItemId,
        string error,
        int maxAttempts,
        CancellationToken cancellationToken = default);
}
