namespace Bookify.Domain.Bookings;

public enum BookingStatus
{
    Processing = 0,
    Reserved = 1,
    Confirmed = 2,
    Rejected = 3,
    Cancelled = 4,
    Completed = 5
}
