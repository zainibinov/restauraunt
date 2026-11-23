using Microsoft.EntityFrameworkCore;

namespace BookingServiceApi;

public class BookingDbContext : DbContext
{
    public BookingDbContext(DbContextOptions<BookingDbContext> options) : base(options) { }

    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingSlot> BookingSlots => Set<BookingSlot>();
    public DbSet<BookingStatusHistory> StatusHistory => Set<BookingStatusHistory>();
    public DbSet<Reminder> Reminders => Set<Reminder>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Booking>(e =>
        {
            e.ToTable("booking");
            e.HasIndex(x => x.IdempotencyKey).IsUnique(); // protects from a double click
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.ConfirmationCode);
            e.Property(x => x.GuestName).HasMaxLength(100);
            e.Property(x => x.GuestPhone).HasMaxLength(20);
            e.Property(x => x.GuestEmail).HasMaxLength(256);
            e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.Property(x => x.ConfirmationCode).HasMaxLength(16);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(50);
        });

        b.Entity<BookingSlot>(e =>
        {
            e.ToTable("booking_slot");
            // UK1 (table_id, slot_datetime): one table cannot be booked twice for the same slot
            e.HasIndex(x => new { x.TableId, x.SlotDateTime }).IsUnique();
            e.HasOne(x => x.Booking).WithMany(x => x.Slots)
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BookingStatusHistory>(e =>
        {
            e.ToTable("booking_status_history");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(50);
            e.HasOne(x => x.Booking).WithMany(x => x.History)
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Reminder>(e =>
        {
            e.ToTable("reminder");
            e.HasOne(x => x.Booking).WithMany(x => x.Reminders)
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
