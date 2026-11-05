using Bookify.Domain.Apartments;
using Bookify.Domain.Bookings;
using Bookify.Infrastructure.Queue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bookify.Infrastructure.Configurations;

internal sealed class BookingQueueMessageConfiguration : IEntityTypeConfiguration<BookingQueueMessage>
{
    public void Configure(EntityTypeBuilder<BookingQueueMessage> builder)
    {
        builder.ToTable("booking_queue");

        builder.HasKey(message => message.Id);

        builder.Property(message => message.Status).HasMaxLength(20);

        builder.HasOne<Booking>()
            .WithMany()
            .HasForeignKey(message => message.BookingId);

        builder.HasOne<Apartment>()
            .WithMany()
            .HasForeignKey(message => message.ApartmentId);

        // Fast lookup of the pending FIFO queue of each apartment.
        builder.HasIndex(message => new { message.ApartmentId, message.CreatedOnUtc })
            .HasFilter("status = 'Pending'");
    }
}
