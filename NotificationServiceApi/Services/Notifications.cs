using StackExchange.Redis;
using Microsoft.Extensions.Options;

namespace NotificationServiceApi;

public class NotificationSettings
{
    public string RestaurantEmail { get; set; } = "hostess@restaurant.local";
    public int MaxAttempts { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 2;
    /// <summary>true = every delivery fails (to see the retries and NotificationFailed in action).</summary>
    public bool SimulateFailure { get; set; }
}

/// <summary>
/// One message to one recipient.
/// Type is unique per booking and recipient/channel, e.g. "created-guest-sms": it is used for the de-duplication key.
/// </summary>
public record Notification(string Type, string Channel, string Recipient, string Text);

// ---------- sending (the real SMS/email providers do not exist, delivery is imitated by a log line) ----------

public interface INotificationSender
{
    Task SendAsync(Notification notification, CancellationToken ct = default);
}

public class LogNotificationSender : INotificationSender
{
    private readonly ILogger<LogNotificationSender> _logger;
    private readonly NotificationSettings _settings;

    public LogNotificationSender(ILogger<LogNotificationSender> logger, IOptions<NotificationSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    public Task SendAsync(Notification notification, CancellationToken ct = default)
    {
        if (_settings.SimulateFailure)
            throw new InvalidOperationException("Simulated delivery failure");

        _logger.LogInformation("[{Channel}] -> {Recipient}: {Text}",
            notification.Channel, notification.Recipient, notification.Text);
        return Task.CompletedTask;
    }
}

// ---------- "was it already sent?" (Redis) ----------

public interface INotificationStateStore
{
    Task<bool> IsSentAsync(Guid bookingId, string type);
    Task MarkSentAsync(Guid bookingId, string type);
}

public class RedisNotificationStateStore : INotificationStateStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(7);
    private readonly IConnectionMultiplexer _redis;

    public RedisNotificationStateStore(IConnectionMultiplexer redis) => _redis = redis;

    private static string Key(Guid bookingId, string type) => $"notification:booking:{bookingId}:{type}";

    public async Task<bool> IsSentAsync(Guid bookingId, string type)
        => await _redis.GetDatabase().KeyExistsAsync(Key(bookingId, type));

    public async Task MarkSentAsync(Guid bookingId, string type)
        => await _redis.GetDatabase().StringSetAsync(Key(bookingId, type), "sent", Ttl);
}

// ---------- retries ----------

public class RetryService
{
    private readonly NotificationSettings _settings;
    private readonly ILogger<RetryService> _logger;

    public RetryService(IOptions<NotificationSettings> settings, ILogger<RetryService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>Tries up to MaxAttempts times, waiting longer after each failure. After the last failure the exception goes up.</summary>
    public async Task ExecuteAsync(Func<Task> action, string what)
    {
        var maxAttempts = Math.Max(1, _settings.MaxAttempts);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                var delay = TimeSpan.FromSeconds(_settings.RetryDelaySeconds * attempt);
                _logger.LogWarning("{What}: attempt {Attempt}/{Max} failed ({Error}); retry in {Delay}s",
                    what, attempt, maxAttempts, ex.Message, delay.TotalSeconds);
                await Task.Delay(delay);
            }
        }
    }
}
