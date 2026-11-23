using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BookingServiceApi;

/// <summary>
/// Listens to the answers of NotificationService.
///   NotificationSent (BookingCreated)  -> the booking becomes Confirmed (changedBy = Guid.Empty, i.e. the system)
///   NotificationFailed                 -> the booking stays Pending (it expires after PendingTimeoutMinutes if nobody confirms it)
/// If RabbitMQ is not available it tries to reconnect every 5 seconds.
/// </summary>
public class NotificationResultConsumer : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly RabbitMqSettings _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationResultConsumer> _logger;

    public NotificationResultConsumer(RabbitMqSettings settings, IServiceScopeFactory scopeFactory,
        ILogger<NotificationResultConsumer> logger)
    {
        _settings = settings;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var connection = RabbitMq.CreateFactory(_settings).CreateConnection();
                using var channel = connection.CreateModel();

                channel.ExchangeDeclare(_settings.Exchange, ExchangeType.Topic, durable: true);
                channel.QueueDeclare(RabbitMq.NotificationResultsQueue, durable: true, exclusive: false, autoDelete: false);
                foreach (var key in RabbitMq.NotificationResultKeys)
                    channel.QueueBind(RabbitMq.NotificationResultsQueue, _settings.Exchange, key);

                channel.BasicQos(0, 5, false);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.Received += (_, ea) => OnReceivedAsync(channel, ea);
                channel.BasicConsume(RabbitMq.NotificationResultsQueue, autoAck: false, consumer);

                _logger.LogInformation("Listening to queue {Queue}", RabbitMq.NotificationResultsQueue);

                var closed = new TaskCompletionSource();
                connection.ConnectionShutdown += (_, _) => closed.TrySetResult();
                using (stoppingToken.Register(() => closed.TrySetResult()))
                    await closed.Task;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("RabbitMQ is not available for the results consumer ({Error}); retry in {Delay}s",
                    ex.Message, ReconnectDelay.TotalSeconds);
            }

            if (stoppingToken.IsCancellationRequested)
                break;

            try { await Task.Delay(ReconnectDelay, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task OnReceivedAsync(IModel channel, BasicDeliverEventArgs ea)
    {
        try
        {
            var json = Encoding.UTF8.GetString(ea.Body.Span);
            await HandleAsync(ea.RoutingKey, json);
            channel.BasicAck(ea.DeliveryTag, multiple: false);
        }
        catch (JsonException ex)
        {
            _logger.LogError("Broken message {Key} dropped: {Error}", ea.RoutingKey, ex.Message);
            channel.BasicAck(ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            // a temporary problem (database...): return the message to the queue and try later
            _logger.LogError("Processing of {Key} failed, the message returns to the queue: {Error}", ea.RoutingKey, ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(5));
            if (channel.IsOpen)
                channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
        }
    }

    private async Task HandleAsync(string routingKey, string json)
    {
        switch (routingKey)
        {
            case "notification.sent":
            {
                var e = JsonSerializer.Deserialize<NotificationSentEvent>(json, RabbitMq.Json)
                        ?? throw new JsonException("Empty message");

                // only the answer to BookingCreated confirms a booking (cancel/reminder answers change nothing)
                if (e.EventType != "BookingCreated")
                    return;

                using var scope = _scopeFactory.CreateScope();
                var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
                try
                {
                    await bookings.ConfirmAsync(e.BookingId, Guid.Empty);
                    _logger.LogInformation("Booking {BookingId} confirmed (notification delivered)", e.BookingId);
                }
                catch (ServiceException ex) when (ex.StatusCode is 404 or 409)
                {
                    // already confirmed, cancelled or expired: the repeated message is harmless
                    _logger.LogInformation("Booking {BookingId} was not confirmed: {Reason}", e.BookingId, ex.Message);
                }
                break;
            }
            case "notification.failed":
            {
                var e = JsonSerializer.Deserialize<NotificationFailedEvent>(json, RabbitMq.Json)
                        ?? throw new JsonException("Empty message");
                _logger.LogWarning("Notification {EventType} for booking {BookingId} failed: {Error}. The booking stays as it is.",
                    e.EventType, e.BookingId, e.Error);
                break;
            }
            default:
                _logger.LogWarning("Unknown routing key {Key}, message ignored", routingKey);
                break;
        }
    }
}
