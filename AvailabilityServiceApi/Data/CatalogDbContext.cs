using Microsoft.EntityFrameworkCore;

namespace AvailabilityServiceApi;

public class CatalogDbContext : DbContext
{
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options) : base(options) { }

    public DbSet<Hall> Halls => Set<Hall>();
    public DbSet<RestaurantTable> Tables => Set<RestaurantTable>();
    public DbSet<WorkingHours> WorkingHours => Set<WorkingHours>();
    public DbSet<TimeSlot> TimeSlots => Set<TimeSlot>();
    public DbSet<BlackoutDate> BlackoutDates => Set<BlackoutDate>();
    public DbSet<BookingDuration> BookingDurations => Set<BookingDuration>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Hall>(e =>
        {
            e.ToTable("hall");
            e.Property(h => h.Name).HasMaxLength(100);
        });

        b.Entity<RestaurantTable>(e =>
        {
            e.ToTable("restaurant_table");
            // UK1 (hall_id, table_number): no two tables with the same number in one hall
            e.HasIndex(t => new { t.HallId, t.TableNumber }).IsUnique();
            // the enum is stored as a string: "Standard", "Vip", "Bar"
            e.Property(t => t.TableType).HasConversion<string>().HasMaxLength(50);
            e.HasOne(t => t.Hall).WithMany(h => h.Tables)
                .HasForeignKey(t => t.HallId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<WorkingHours>(e =>
        {
            e.ToTable("working_hours");
            e.HasIndex(w => w.DayOfWeek).IsUnique(); // one row per weekday
        });

        b.Entity<TimeSlot>(e =>
        {
            e.ToTable("time_slot");
            e.Property(s => s.SlotDate).HasColumnType("date");
            // protects against generating the same slot twice
            e.HasIndex(s => new { s.WorkingHoursId, s.SlotDate, s.SlotTime }).IsUnique();
            e.HasOne(s => s.WorkingHours).WithMany(w => w.TimeSlots)
                .HasForeignKey(s => s.WorkingHoursId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BlackoutDate>(e =>
        {
            e.ToTable("blackout_date");
            e.Property(d => d.Date).HasColumnType("date");
            e.Property(d => d.Reason).HasMaxLength(200);
            e.HasOne(d => d.Hall).WithMany(h => h.BlackoutDates)
                .HasForeignKey(d => d.HallId).IsRequired(false).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BookingDuration>(e => e.ToTable("booking_duration"));
    }
}
