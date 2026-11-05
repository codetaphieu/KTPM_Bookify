using Bookify.Application.Abstractions.Messaging;

namespace Bookify.Application.Bookings.ReserveBooking;

public sealed record ReserveBookingCommand(
    Guid ApartmentId,
    Guid UserId,
    DateOnly StartDate,
    DateOnly EndDate,
    string? IdempotencyKey = null) : ICommand<Guid>;
