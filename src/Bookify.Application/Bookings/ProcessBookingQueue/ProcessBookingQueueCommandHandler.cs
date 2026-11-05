using Bookify.Application.Abstractions.Clock;
using Bookify.Application.Abstractions.Messaging;
using Bookify.Application.Abstractions.Queue;
using Bookify.Domain.Abstractions;
using Bookify.Domain.Apartments;
using Bookify.Domain.Bookings;

namespace Bookify.Application.Bookings.ProcessBookingQueue;

// Processes the pending bookings of ONE apartment in FIFO order.
// The caller guarantees only one worker runs this for a given apartment at a time,
// so the overlap check is done on real date ranges, not on the apartment row version.
internal sealed class ProcessBookingQueueCommandHandler : ICommandHandler<ProcessBookingQueueCommand, int>
{
    // A booking that keeps failing is given up after this many runs so it cannot block its apartment.
    internal const int MaxAttempts = 5;

    private readonly IApartmentRepository _apartmentRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly IBookingQueue _bookingQueue;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ProcessBookingQueueCommandHandler(
        IApartmentRepository apartmentRepository,
        IBookingRepository bookingRepository,
        IBookingQueue bookingQueue,
        IUnitOfWork unitOfWork,
        IDateTimeProvider dateTimeProvider)
    {
        _apartmentRepository = apartmentRepository;
        _bookingRepository = bookingRepository;
        _bookingQueue = bookingQueue;
        _unitOfWork = unitOfWork;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<int>> Handle(ProcessBookingQueueCommand request, CancellationToken cancellationToken)
    {
        IReadOnlyList<BookingQueueItem> pendingItems = await _bookingQueue.GetPendingItemsAsync(
            request.ApartmentId,
            cancellationToken);

        Apartment? apartment = await _apartmentRepository.GetByIdAsync(request.ApartmentId, cancellationToken);

        if (apartment is null)
        {
            // Nothing can ever be booked here: close the queue so it is not retried forever.
            foreach (BookingQueueItem item in pendingItems)
            {
                await _bookingQueue.MarkFailedAsync(item.Id, ApartmentErrors.NotFound.Name, cancellationToken);
            }

            return Result.Failure<int>(ApartmentErrors.NotFound);
        }

        int processedCount = 0;

        foreach (BookingQueueItem item in pendingItems)
        {
            try
            {
                if (await ProcessItemAsync(apartment, item, cancellationToken))
                {
                    processedCount++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await _bookingQueue.RecordFailedAttemptAsync(item.Id, exception.Message, MaxAttempts, cancellationToken);

                // Stop this apartment: the unit of work may hold the failed changes, and later items
                // must not overtake this one (FIFO). The next run retries it, or skips it once dead-lettered.
                return processedCount;
            }
        }

        return processedCount;
    }

    // Returns true when the booking got its final status (Reserved or Rejected) in this run.
    private async Task<bool> ProcessItemAsync(Apartment apartment, BookingQueueItem item, CancellationToken cancellationToken)
    {
        Booking? booking = await _bookingRepository.GetByIdAsync(item.BookingId, cancellationToken);

        if (booking is null)
        {
            await _bookingQueue.MarkFailedAsync(item.Id, BookingErrors.NotFound.Name, cancellationToken);
            return false;
        }

        // Already decided (e.g. the item was retried after a crash): just close the queue item.
        if (booking.Status != BookingStatus.Processing)
        {
            await _bookingQueue.MarkCompletedAsync(item.Id, cancellationToken);
            return false;
        }

        DateTime utcNow = _dateTimeProvider.UtcNow;

        bool isOverlapping = await _bookingRepository.IsOverlappingAsync(
            apartment,
            booking.Duration,
            cancellationToken);

        if (isOverlapping)
        {
            booking.RejectFromProcessing(utcNow);
        }
        else
        {
            booking.ReserveFromProcessing(apartment, utcNow);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _bookingQueue.MarkCompletedAsync(item.Id, cancellationToken);

        return true;
    }
}
