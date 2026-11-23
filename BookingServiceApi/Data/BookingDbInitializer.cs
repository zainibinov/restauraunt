using Microsoft.EntityFrameworkCore;

namespace BookingServiceApi;

/// <summary>On startup: creates the schema if it does not exist.</summary>
public class BookingDbInitializer : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public BookingDbInitializer(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        await db.Database.EnsureCreatedAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
