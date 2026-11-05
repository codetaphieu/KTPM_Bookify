namespace Bookify.Infrastructure.Queue;

public sealed class BookingQueueMessage
{
    public const string Pending = "Pending";
    public const string Completed = "Completed";
    public const string Failed = "Failed";

    public BookingQueueMessage(Guid id, Guid bookingId, Guid apartmentId, DateTime createdOnUtc)
    {
        Id = id;
        BookingId = bookingId;
        ApartmentId = apartmentId;
        CreatedOnUtc = createdOnUtc;
        Status = Pending;
    }

    public Guid Id { get; init; }

    public Guid BookingId { get; init; }

    // Partition key: messages of one apartment are processed sequentially.
    public Guid ApartmentId { get; init; }

    public string Status { get; init; }

    // Failed processing attempts; the message is dead-lettered after the limit.
    public int Attempts { get; init; }

    public DateTime CreatedOnUtc { get; init; }

    public DateTime? ProcessedOnUtc { get; init; }

    public string? Error { get; init; }
}
