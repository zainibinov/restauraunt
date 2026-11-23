namespace BookingServiceApi;

/// <summary>Every 30 seconds publishes BookingReminder for the reminders whose time has come.</summary>
public class BookingReminderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BookingReminderWorker> _logger;

    public BookingReminderWorker(IServiceScopeFactory scopeFactory, ILogger<BookingReminderWorker> logger)
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
                var count = await bookings.SendDueRemindersAsync(stoppingToken);
                if (count > 0)
                    _logger.LogInformation("Published {Count} reminders", count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Sending reminders failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
