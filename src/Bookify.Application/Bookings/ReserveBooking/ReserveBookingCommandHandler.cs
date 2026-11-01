using System.Data;
using Bookify.Application.Abstractions.Clock;
using Bookify.Application.Abstractions.Data;
using Bookify.Application.Abstractions.Messaging;
using Bookify.Application.Exceptions;
using Bookify.Domain.Abstractions;
using Bookify.Domain.Apartments;
using Bookify.Domain.Bookings;
using Bookify.Domain.Users;
using Dapper;

namespace Bookify.Application.Bookings.ReserveBooking;

internal sealed class ReserveBookingCommandHandler : ICommandHandler<ReserveBookingCommand, Guid>
{
    private readonly IUserRepository _userRepository;
    private readonly IApartmentRepository _apartmentRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly PricingService _pricingService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ISqlConnectionFactory _sqlConnectionFactory;

    public ReserveBookingCommandHandler(
        IUserRepository userRepository,
        IApartmentRepository apartmentRepository,
        IBookingRepository bookingRepository,
        IUnitOfWork unitOfWork,
        PricingService pricingService,
        IDateTimeProvider dateTimeProvider,
        ISqlConnectionFactory sqlConnectionFactory)
    {
        _userRepository = userRepository;
        _apartmentRepository = apartmentRepository;
        _bookingRepository = bookingRepository;
        _unitOfWork = unitOfWork;
        _pricingService = pricingService;
        _dateTimeProvider = dateTimeProvider;
        _sqlConnectionFactory = sqlConnectionFactory;
    }

    public async Task<Result<Guid>> Handle(ReserveBookingCommand request, CancellationToken cancellationToken)
    {
        User? user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<Guid>(UserErrors.NotFound);
        }

        Apartment? apartment = await _apartmentRepository.GetByIdAsync(request.ApartmentId, cancellationToken);

        if (apartment is null)
        {
            return Result.Failure<Guid>(ApartmentErrors.NotFound);
        }

        var duration = DateRange.Create(request.StartDate, request.EndDate);

        if (await _bookingRepository.IsOverlappingAsync(apartment, duration, cancellationToken))
        {
            return Result.Failure<Guid>(BookingErrors.Overlap);
        }

        try
        {
            var booking = Booking.Reserve(
                apartment,
                user.Id,
                duration,
                _dateTimeProvider.UtcNow,
                _pricingService);

            _bookingRepository.Add(booking);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Đồng bộ trạng thái bận vào bảng Read Model (apartment_daily_availability)
            const string updateAvailabilitySql = """
                UPDATE apartment_daily_availability
                SET is_available = FALSE,
                    booking_id = @BookingId
                WHERE apartment_id = @ApartmentId
                  AND date >= @StartDate
                  AND date < @EndDate;
                """;

            using IDbConnection connection = _sqlConnectionFactory.CreateConnection();
            await connection.ExecuteAsync(updateAvailabilitySql, new
            {
                BookingId = booking.Id,
                ApartmentId = request.ApartmentId,
                StartDate = request.StartDate.ToDateTime(TimeOnly.MinValue),
                EndDate = request.EndDate.ToDateTime(TimeOnly.MinValue)
            });

            return booking.Id;
        }
        catch (ConcurrencyException)
        {
            return Result.Failure<Guid>(BookingErrors.Overlap);
        }
    }
}