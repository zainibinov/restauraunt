namespace BookingServiceApi;

/// <summary>Every minute cancels Pending bookings that were not confirmed within PendingTimeoutMinutes.</summary>
public class BookingExpirationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BookingExpirationWorker> _logger;

    public BookingExpirationWorker(IServiceScopeFactory scopeFactory, ILogger<BookingExpirationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
                var count = await bookings.ExpirePendingAsync(stoppingToken);
                if (count > 0)
                    _logger.LogInformation("Expired {Count} pending bookings", count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Booking expiration failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
