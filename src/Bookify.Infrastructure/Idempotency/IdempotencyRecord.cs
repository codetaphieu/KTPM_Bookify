namespace Bookify.Infrastructure.Idempotency;

public sealed class IdempotencyRecord
{
    public IdempotencyRecord(Guid userId, string key, Guid bookingId, DateTime createdOnUtc)
    {
        UserId = userId;
        Key = key;
        BookingId = bookingId;
        CreatedOnUtc = createdOnUtc;
    }

    public Guid UserId { get; init; }

    public string Key { get; init; }

    public Guid BookingId { get; init; }

    public DateTime CreatedOnUtc { get; init; }
}
