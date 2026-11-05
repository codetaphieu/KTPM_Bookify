namespace Bookify.Application.Abstractions.Idempotency;

public interface IIdempotencyService
{
    Task<Guid?> GetBookingIdAsync(Guid userId, string key, CancellationToken cancellationToken = default);

    // Persisted together with the booking by IUnitOfWork.SaveChangesAsync (same transaction).
    void Register(Guid userId, string key, Guid bookingId);
}
