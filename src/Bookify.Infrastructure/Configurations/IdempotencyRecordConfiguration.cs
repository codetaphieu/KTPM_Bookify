using Bookify.Domain.Bookings;
using Bookify.Domain.Users;
using Bookify.Infrastructure.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bookify.Infrastructure.Configurations;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_records");

        // The primary key makes concurrent requests with the same key fail atomically.
        builder.HasKey(record => new { record.UserId, record.Key });

        builder.Property(record => record.Key).HasMaxLength(200);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(record => record.UserId);

        builder.HasOne<Booking>()
            .WithMany()
            .HasForeignKey(record => record.BookingId);
    }
}
