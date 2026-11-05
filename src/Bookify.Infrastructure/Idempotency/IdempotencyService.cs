using Bookify.Application.Abstractions.Clock;
using Bookify.Application.Abstractions.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace Bookify.Infrastructure.Idempotency;

internal sealed class IdempotencyService : IIdempotencyService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public IdempotencyService(ApplicationDbContext dbContext, IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Guid?> GetBookingIdAsync(Guid userId, string key, CancellationToken cancellationToken = default)
    {
        return await _dbContext
            .Set<IdempotencyRecord>()
            .AsNoTracking()
            .Where(record => record.UserId == userId && record.Key == key)
            .Select(record => (Guid?)record.BookingId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public void Register(Guid userId, string key, Guid bookingId)
    {
        _dbContext.Add(new IdempotencyRecord(userId, key, bookingId, _dateTimeProvider.UtcNow));
    }
}
