using Microsoft.EntityFrameworkCore;

namespace AvailabilityServiceApi;

/// <summary>On startup: creates the schema and puts in some starter data so that there is something to test.</summary>
public class CatalogDbInitializer : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public CatalogDbInitializer(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        await db.Database.EnsureCreatedAsync(cancellationToken);

        // working hours: every day 12:00-23:00, step 30 minutes
        if (!await db.WorkingHours.AnyAsync(cancellationToken))
        {
            for (var day = 0; day <= 6; day++)
            {
                db.WorkingHours.Add(new WorkingHours
                {
                    Id = Guid.NewGuid(),
                    DayOfWeek = day,
                    OpenTime = TimeSpan.FromHours(12),
                    CloseTime = TimeSpan.FromHours(23),
                    SlotStepMinutes = 30
                });
            }
        }

        // a table is occupied for 2 hours + 15 minutes for cleaning
        if (!await db.BookingDurations.AnyAsync(cancellationToken))
        {
            db.BookingDurations.Add(new BookingDuration
            {
                Id = Guid.NewGuid(),
                DefaultDurationMinutes = 120,
                CleaningBufferMinutes = 15
            });
        }

        // demo hall with four tables (delete or change as you like)
        if (!await db.Halls.AnyAsync(cancellationToken))
        {
            var hall = new Hall { Id = Guid.NewGuid(), Name = "Main hall", Description = "Demo hall" };
            hall.Tables.AddRange(new[]
            {
                new RestaurantTable { Id = Guid.NewGuid(), TableNumber = 1, Capacity = 2, TableType = TableType.Standard },
                new RestaurantTable { Id = Guid.NewGuid(), TableNumber = 2, Capacity = 4, TableType = TableType.Standard },
                new RestaurantTable { Id = Guid.NewGuid(), TableNumber = 3, Capacity = 6, TableType = TableType.Vip },
                new RestaurantTable { Id = Guid.NewGuid(), TableNumber = 4, Capacity = 2, TableType = TableType.Bar }
            });
            db.Halls.Add(hall);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
