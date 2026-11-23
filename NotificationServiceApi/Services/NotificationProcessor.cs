using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace NotificationServiceApi;

/// <summary>
/// The brain of the service: an event comes in -> the messages are built from templates -> each message is sent once
/// (the "already sent" mark is in Redis) with retries -> the result (Sent / Failed) is published back.
/// </summary>
public class NotificationProcessor
{
    private readonly INotificationSender _sender;
    private readonly INotificationStateStore _state;
    private readonly RetryService _retry;
    private readonly RabbitMqEventPublisher _results;
    private readonly NotificationSettings _settings;
    private readonly ILogger<NotificationProcessor> _logger;

    public NotificationProcessor(INotificationSender sender, INotificationStateStore state, RetryService retry,
        RabbitMqEventPublisher results, IOptions<NotificationSettings> settings, ILogger<NotificationProcessor> logger)
    {
        _sender = sender;
        _state = state;
        _retry = retry;
        _results = results;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>Throws JsonException if the message is broken (the caller drops such a message).</summary>
    public async Task HandleAsync(string routingKey, string json)
    {
        switch (routingKey)
        {
            case "booking.created":
            {
                var e = Parse<BookingCreatedEvent>(json);
                var when = Fmt(e.StartTime);
                await ProcessAsync(e.BookingId, "BookingCreated", new List<Notification>
                {
                    new("created-guest-sms", "SMS", e.GuestPhone,
                        $"{e.GuestName}, ваша бронь принята: {when}, гостей: {e.GuestsCount}. Код подтверждения: {e.ConfirmationCode}."),
                    new("created-guest-email", "Email", e.GuestEmail,
                        $"Здравствуйте, {e.GuestName}! Ваша бронь на {when} принята (гостей: {e.GuestsCount}). Код подтверждения: {e.ConfirmationCode}."),
                    new("created-restaurant", "Email", _settings.RestaurantEmail,
                        $"Новая бронь: {e.GuestName} ({e.GuestPhone}), {when}, гостей: {e.GuestsCount}, стол {Short(e.TableId)}.")
                });
                break;
            }
            case "booking.cancelled":
            {
                var e = Parse<BookingCancelledEvent>(json);
                var when = Fmt(e.StartTime);
                await ProcessAsync(e.BookingId, "BookingCancelled", new List<Notification>
                {
                    new("cancelled-guest-sms", "SMS", e.GuestPhone,
                        $"{e.GuestName}, ваша бронь на {when} отменена. Причина: {e.Reason}."),
                    new("cancelled-guest-email", "Email", e.GuestEmail,
                        $"Здравствуйте, {e.GuestName}! Ваша бронь на {when} отменена. Причина: {e.Reason}."),
                    new("cancelled-restaurant", "Email", _settings.RestaurantEmail,
                        $"Бронь отменена: {e.GuestName} ({e.GuestPhone}), {when}. Причина: {e.Reason}.")
                });
                break;
            }
            case "booking.reminder":
            {
                var e = Parse<BookingReminderEvent>(json);
                var when = Fmt(e.StartTime);
                await ProcessAsync(e.BookingId, "BookingReminder", new List<Notification>
                {
                    new("reminder-guest-sms", "SMS", e.GuestPhone,
                        $"{e.GuestName}, напоминаем: ждём вас {when}, гостей: {e.GuestsCount}."),
                    new("reminder-guest-email", "Email", e.GuestEmail,
                        $"Здравствуйте, {e.GuestName}! Напоминаем о брони на {when} (гостей: {e.GuestsCount}). Ждём вас!")
                });
                break;
            }
            default:
                _logger.LogWarning("Unknown routing key {Key}, message ignored", routingKey);
                break;
        }
    }

    private async Task ProcessAsync(Guid bookingId, string eventType, List<Notification> notifications)
    {
        var errors = new List<string>();

        foreach (var n in notifications)
        {
            // the same event delivered twice must not produce the same message twice
            if (await _state.IsSentAsync(bookingId, n.Type))
            {
                _logger.LogInformation("Already sent, skipped: {Type} for booking {BookingId}", n.Type, bookingId);
                continue;
            }

            try
            {
                await _retry.ExecuteAsync(() => _sender.SendAsync(n), $"{n.Type} (booking {bookingId})");
                await _state.MarkSentAsync(bookingId, n.Type);
            }
            catch (Exception ex)
            {
                _logger.LogError("Delivery failed: {Type} for booking {BookingId}: {Error}", n.Type, bookingId, ex.Message);
                errors.Add($"{n.Type}: {ex.Message}");
            }
        }

        // If this publish fails, the exception goes up, the message returns to the queue and is processed again:
        // the "already sent" marks make sure that nothing is sent twice.
        if (errors.Count == 0)
            _results.Publish(new NotificationSentEvent(bookingId, eventType, DateTime.UtcNow));
        else
            _results.Publish(new NotificationFailedEvent(bookingId, eventType, string.Join("; ", errors)));
    }

    private static T Parse<T>(string json) where T : class
        => JsonSerializer.Deserialize<T>(json, RabbitMq.Json) ?? throw new JsonException("Empty message");

    private static string Fmt(DateTime time) => time.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    private static string Short(Guid id) => id.ToString()[..8];
}
