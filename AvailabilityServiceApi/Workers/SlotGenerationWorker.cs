namespace AvailabilityServiceApi;

/// <summary>Periodically generates slots for the coming days.</summary>
public class SlotGenerationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<SlotGenerationWorker> _logger;

    public SlotGenerationWorker(IServiceScopeFactory scopeFactory, IConfiguration config,
        ILogger<SlotGenerationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var daysAhead = _config.GetValue("SlotGeneration:DaysAhead", 14);
        var interval = TimeSpan.FromMinutes(_config.GetValue("SlotGeneration:IntervalMinutes", 360));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var slotService = scope.ServiceProvider.GetRequiredService<SlotService>();
                var created = await slotService.GenerateAheadAsync(daysAhead, stoppingToken);
                _logger.LogInformation("Slot generation finished, created {Count} slots", created);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Slot generation failed");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
