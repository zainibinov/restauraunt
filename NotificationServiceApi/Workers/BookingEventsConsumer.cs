using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NotificationServiceApi;

/// <summary>
/// Listens to the queue with the BookingService events and passes every message to NotificationProcessor.
/// If RabbitMQ is not available (or the connection is lost) it tries to reconnect every 5 seconds.
/// </summary>
public class BookingEventsConsumer : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly RabbitMqSettings _settings;
    private readonly NotificationProcessor _processor;
    private readonly ILogger<BookingEventsConsumer> _logger;

    public BookingEventsConsumer(IOptions<RabbitMqSettings> settings, NotificationProcessor processor,
        ILogger<BookingEventsConsumer> logger)
    {
        _settings = settings.Value;
        _processor = processor;
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

                // the exchange, the queue and the bindings are created if they do not exist yet
                channel.ExchangeDeclare(_settings.Exchange, ExchangeType.Topic, durable: true);
                channel.QueueDeclare(RabbitMq.BookingEventsQueue, durable: true, exclusive: false, autoDelete: false);
                foreach (var key in RabbitMq.BookingRoutingKeys)
                    channel.QueueBind(RabbitMq.BookingEventsQueue, _settings.Exchange, key);

                channel.BasicQos(0, 5, false); // not more than 5 unconfirmed messages at a time

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.Received += (_, ea) => OnReceivedAsync(channel, ea);
                channel.BasicConsume(RabbitMq.BookingEventsQueue, autoAck: false, consumer);

                _logger.LogInformation("Listening to queue {Queue} on {Host}:{Port}",
                    RabbitMq.BookingEventsQueue, _settings.Host, _settings.Port);

                // wait until the service stops or the connection is lost
                var closed = new TaskCompletionSource();
                connection.ConnectionShutdown += (_, _) => closed.TrySetResult();
                using (stoppingToken.Register(() => closed.TrySetResult()))
                    await closed.Task;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("RabbitMQ is not available ({Error}); retry in {Delay}s", ex.Message, ReconnectDelay.TotalSeconds);
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
            await _processor.HandleAsync(ea.RoutingKey, json);
            channel.BasicAck(ea.DeliveryTag, multiple: false);
        }
        catch (JsonException ex)
        {
            // a broken message will not get better by repeating: drop it
            _logger.LogError("Broken message {Key} dropped: {Error}", ea.RoutingKey, ex.Message);
            channel.BasicAck(ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            // a temporary problem (Redis, RabbitMQ...): return the message to the queue and try later
            _logger.LogError("Processing of {Key} failed, the message returns to the queue: {Error}", ea.RoutingKey, ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(5));
            if (channel.IsOpen)
                channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
        }
    }
}
