namespace NotificationServiceApi;

// The same messages as in BookingService (Contracts/Events.cs). They travel as JSON,
// so only the names and types of the fields matter, not the namespace.

public record BookingCreatedEvent(
    Guid BookingId, Guid UserId,
    string GuestName, string GuestPhone, string GuestEmail,
    Guid TableId, DateTime StartTime, DateTime EndTime,
    int GuestsCount, string ConfirmationCode);

public record BookingCancelledEvent(
    Guid BookingId,
    string GuestName, string GuestPhone, string GuestEmail,
    DateTime StartTime, string Reason);

public record BookingReminderEvent(
    Guid BookingId,
    string GuestName, string GuestPhone, string GuestEmail,
    DateTime StartTime, int GuestsCount);

// What this service publishes back.
public record NotificationSentEvent(Guid BookingId, string EventType, DateTime SentAt);

public record NotificationFailedEvent(Guid BookingId, string EventType, string Error);
