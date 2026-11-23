namespace BookingServiceApi;

// Messages for RabbitMQ (stage 5). For now they are only written to the log.

public record BookingCreatedEvent(
    Guid BookingId, Guid UserId,
    string GuestName, string GuestPhone, string GuestEmail,
    Guid TableId, DateTime StartTime, DateTime EndTime,
    int GuestsCount, string ConfirmationCode);

public record BookingCancelledEvent(
    Guid BookingId,
    string GuestName, string GuestPhone, string GuestEmail,
    DateTime StartTime, string Reason);

/// <summary>N hours before the visit (BookingService publishes it at the next step).</summary>
public record BookingReminderEvent(
    Guid BookingId,
    string GuestName, string GuestPhone, string GuestEmail,
    DateTime StartTime, int GuestsCount);

// What NotificationService answers (BookingService will consume them at the next step).
public record NotificationSentEvent(Guid BookingId, string EventType, DateTime SentAt);

public record NotificationFailedEvent(Guid BookingId, string EventType, string Error);
