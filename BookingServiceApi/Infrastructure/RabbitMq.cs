using System.Text.Json;
using RabbitMQ.Client;

namespace BookingServiceApi;

public class RabbitMqSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string User { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    /// <summary>One topic exchange for all events: booking.created, booking.cancelled, booking.reminder, notification.sent, notification.failed.</summary>
    public string Exchange { get; set; } = "restaurant.events";
}

public static class RabbitMq
{
    /// <summary>The queue of this service: it receives the answers of NotificationService.</summary>
    public const string NotificationResultsQueue = "booking.notification-results";

    public static readonly string[] NotificationResultKeys = { "notification.sent", "notification.failed" };

    /// <summary>The same JSON options (camelCase) must be used on both sides: BookingService and NotificationService.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ConnectionFactory CreateFactory(RabbitMqSettings s) => new()
    {
        HostName = s.Host,
        Port = s.Port,
        UserName = s.User,
        Password = s.Password,
        DispatchConsumersAsync = true,
        RequestedConnectionTimeout = TimeSpan.FromSeconds(3)
    };

    /// <summary>Which routing key belongs to which event.</summary>
    public static string RoutingKeyFor(object message) => message switch
    {
        BookingCreatedEvent => "booking.created",
        BookingCancelledEvent => "booking.cancelled",
        BookingReminderEvent => "booking.reminder",
        _ => throw new InvalidOperationException($"No routing key for {message.GetType().Name}")
    };
}

/// <summary>
/// The real IMessagePublisher: sends the events to RabbitMQ.
/// The connection is opened on the first publish (and reopened after a failure), so BookingService starts even if RabbitMQ is down;
/// a failed publish is only logged by BookingService (SafeAsync), the booking itself stays saved.
/// </summary>
public sealed class RabbitMqMessagePublisher : IMessagePublisher, IDisposable
{
    private readonly RabbitMqSettings _settings;
    private readonly object _gate = new(); // IModel is not thread-safe
    private IConnection? _connection;
    private IModel? _channel;

    public RabbitMqMessagePublisher(RabbitMqSettings settings) => _settings = settings;

    public Task PublishAsync<T>(T message, CancellationToken ct = default) where T : class
    {
        var routingKey = RabbitMq.RoutingKeyFor(message);
        var body = JsonSerializer.SerializeToUtf8Bytes(message, message.GetType(), RabbitMq.Json);

        lock (_gate)
        {
            try
            {
                var channel = GetChannel();
                var props = channel.CreateBasicProperties();
                props.Persistent = true; // the message survives a broker restart
                props.ContentType = "application/json";
                props.MessageId = Guid.NewGuid().ToString();
                channel.BasicPublish(_settings.Exchange, routingKey, props, body);
            }
            catch
            {
                Reset(); // the next publish opens a fresh connection
                throw;
            }
        }
        return Task.CompletedTask;
    }

    private IModel GetChannel()
    {
        if (_channel is { IsOpen: true })
            return _channel;

        Reset();
        _connection = RabbitMq.CreateFactory(_settings).CreateConnection();
        _channel = _connection.CreateModel();
        _channel.ExchangeDeclare(_settings.Exchange, ExchangeType.Topic, durable: true);
        return _channel;
    }

    private void Reset()
    {
        try { _channel?.Dispose(); } catch { /* already broken */ }
        try { _connection?.Dispose(); } catch { /* already broken */ }
        _channel = null;
        _connection = null;
    }

    public void Dispose()
    {
        lock (_gate) Reset();
    }
}
