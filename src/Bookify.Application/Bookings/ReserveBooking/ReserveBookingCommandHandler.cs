using Bookify.Application.Abstractions.Clock;
using Bookify.Application.Abstractions.Idempotency;
using Bookify.Application.Abstractions.Messaging;
using Bookify.Application.Abstractions.Queue;
using Bookify.Application.Exceptions;
using Bookify.Domain.Abstractions;
using Bookify.Domain.Apartments;
using Bookify.Domain.Bookings;
using Bookify.Domain.Users;

namespace Bookify.Application.Bookings.ReserveBooking;

// Fast path: validate, create the booking as Processing, enqueue it per apartment and return.
// The overlap check happens later in ProcessBookingQueueCommandHandler, one apartment at a time.
internal sealed class ReserveBookingCommandHandler : ICommandHandler<ReserveBookingCommand, Guid>
{
    private readonly IUserRepository _userRepository;
    private readonly IApartmentRepository _apartmentRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly PricingService _pricingService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IBookingQueue _bookingQueue;
    private readonly IIdempotencyService _idempotencyService;

    public ReserveBookingCommandHandler(
        IUserRepository userRepository,
        IApartmentRepository apartmentRepository,
        IBookingRepository bookingRepository,
        IUnitOfWork unitOfWork,
        PricingService pricingService,
        IDateTimeProvider dateTimeProvider,
        IBookingQueue bookingQueue,
        IIdempotencyService idempotencyService)
    {
        _userRepository = userRepository;
        _apartmentRepository = apartmentRepository;
        _bookingRepository = bookingRepository;
        _unitOfWork = unitOfWork;
        _pricingService = pricingService;
        _dateTimeProvider = dateTimeProvider;
        _bookingQueue = bookingQueue;
        _idempotencyService = idempotencyService;
    }

    public async Task<Result<Guid>> Handle(ReserveBookingCommand request, CancellationToken cancellationToken)
    {
        bool hasIdempotencyKey = !string.IsNullOrWhiteSpace(request.IdempotencyKey);

        if (hasIdempotencyKey)
        {
            Guid? existingBookingId = await _idempotencyService.GetBookingIdAsync(
                request.UserId,
                request.IdempotencyKey!,
                cancellationToken);

            if (existingBookingId.HasValue)
            {
                return existingBookingId.Value;
            }
        }

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

        var booking = Booking.CreateProcessing(
            apartment,
            user.Id,
            duration,
            _dateTimeProvider.UtcNow,
            _pricingService);

        _bookingRepository.Add(booking);

        _bookingQueue.Enqueue(booking.Id, apartment.Id);

        if (hasIdempotencyKey)
        {
            _idempotencyService.Register(user.Id, request.IdempotencyKey!, booking.Id);
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException) when (hasIdempotencyKey)
        {
            // A concurrent request with the same key won the race: return its booking.
            Guid? existingBookingId = await _idempotencyService.GetBookingIdAsync(
                request.UserId,
                request.IdempotencyKey!,
                cancellationToken);

            if (existingBookingId.HasValue)
            {
                return existingBookingId.Value;
            }

            throw;
        }

        return booking.Id;
    }
}
