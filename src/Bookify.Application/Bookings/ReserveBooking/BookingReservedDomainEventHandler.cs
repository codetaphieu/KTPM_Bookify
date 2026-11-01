using System.Data;
using Bookify.Application.Abstractions.Data;
using Bookify.Application.Abstractions.Email;
using Bookify.Domain.Bookings;
using Bookify.Domain.Bookings.Events;
using Bookify.Domain.Users;
using Dapper;
using MediatR;

namespace Bookify.Application.Bookings.ReserveBooking;

internal sealed class BookingReservedDomainEventHandler : INotificationHandler<BookingReservedDomainEvent>
{
    private readonly IBookingRepository _bookingRepository;
    private readonly IUserRepository _userRepository;
    private readonly IEmailService _emailService;
    private readonly ISqlConnectionFactory _sqlConnectionFactory;

    public BookingReservedDomainEventHandler(
        IBookingRepository bookingRepository,
        IUserRepository userRepository,
        IEmailService emailService,
        ISqlConnectionFactory sqlConnectionFactory)
    {
        _bookingRepository = bookingRepository;
        _userRepository = userRepository;
        _emailService = emailService;
        _sqlConnectionFactory = sqlConnectionFactory;
    }

    public async Task Handle(BookingReservedDomainEvent notification, CancellationToken cancellationToken)
    {
        Booking? booking = await _bookingRepository.GetByIdAsync(notification.BookingId, cancellationToken);

        if (booking is null)
        {
            return;
        }

        User? user = await _userRepository.GetByIdAsync(booking.UserId, cancellationToken);

        if (user is null)
        {
            return;
        }

        await _emailService.SendAsync(
            user.Email,
            "Booking reserved!",
            "You have 10 minutes to confirm this booking");

        List<DateTime> bookedDates = new();
        for (DateOnly date = booking.Duration.Start; date <= booking.Duration.End; date = date.AddDays(1))
        {
            bookedDates.Add(date.ToDateTime(TimeOnly.MinValue));
        }

        using IDbConnection connection = _sqlConnectionFactory.CreateConnection();
        const string sql = """
            INSERT INTO apartment_booked_dates (apartment_id, date, booking_id)
            VALUES (@ApartmentId, @Date, @BookingId)
            ON CONFLICT DO NOTHING;
            """;

        IEnumerable<object> records = bookedDates.Select(d => new
        {
            booking.ApartmentId,
            Date = d,
            BookingId = booking.Id
        });

        await connection.ExecuteAsync(sql, records);
    }
}