using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace NotificationServiceApi;

public class RabbitMqSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string User { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    /// <summary>The same exchange as in BookingService.</summary>
    public string Exchange { get; set; } = "restaurant.events";
}

public static class RabbitMq
{
    /// <summary>The queue of this service: it receives everything BookingService publishes.</summary>
    public const string BookingEventsQueue = "notification.booking-events";

    public static readonly string[] BookingRoutingKeys = { "booking.created", "booking.cancelled", "booking.reminder" };

    /// <summary>The same JSON options (camelCase) as in BookingService.</summary>
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
}

/// <summary>Publishes the delivery results (NotificationSent / NotificationFailed) back to the exchange.</summary>
public sealed class RabbitMqEventPublisher : IDisposable
{
    private readonly RabbitMqSettings _settings;
    private readonly object _gate = new(); // IModel is not thread-safe
    private IConnection? _connection;
    private IModel? _channel;

    public RabbitMqEventPublisher(IOptions<RabbitMqSettings> settings) => _settings = settings.Value;

    public void Publish(object message)
    {
        var routingKey = message switch
        {
            NotificationSentEvent => "notification.sent",
            NotificationFailedEvent => "notification.failed",
            _ => throw new InvalidOperationException($"No routing key for {message.GetType().Name}")
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(message, message.GetType(), RabbitMq.Json);

        lock (_gate)
        {
            try
            {
                var channel = GetChannel();
                var props = channel.CreateBasicProperties();
                props.Persistent = true;
                props.ContentType = "application/json";
                props.MessageId = Guid.NewGuid().ToString();
                channel.BasicPublish(_settings.Exchange, routingKey, props, body);
            }
            catch
            {
                Reset();
                throw;
            }
        }
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
