using Bookify.Application.Abstractions.Messaging;

namespace Bookify.Application.Bookings.ProcessBookingQueue;

public sealed record ProcessBookingQueueCommand(Guid ApartmentId) : ICommand<int>;
