namespace BookingServiceApi;

// The contracts of everything BookingService depends on.
// BookingService knows ONLY these interfaces. At stage 5 the fakes (Fakes.cs)
// are replaced by real implementations (gRPC, Redis, RabbitMQ, SignalR) and BookingService does not change.

// ---------- AuthService (later: gRPC) ----------

public record UserContact(Guid UserId, string FullName, string Phone, string Email);

public interface IAuthClient
{
    Task<UserContact?> GetUserContactAsync(Guid userId, CancellationToken ct = default);
}

// ---------- AvailabilityService (later: gRPC) ----------

public record TableInfo(Guid TableId, Guid HallId, int Capacity, string TableType);

/// <summary>SlotStepMinutes - the grid step, DurationMinutes - how long a guest stays, CleaningBufferMinutes - cleaning after.</summary>
public record BookingParameters(int SlotStepMinutes, int DurationMinutes, int CleaningBufferMinutes);

public interface IAvailabilityClient
{
    /// <summary>null = there is no such table.</summary>
    Task<TableInfo?> GetTableInfoAsync(Guid tableId, CancellationToken ct = default);

    /// <summary>The parameters of the given date (the slot step may differ by weekday). If the restaurant is closed - ServiceException 409.</summary>
    Task<BookingParameters> GetBookingParametersAsync(DateTime date, CancellationToken ct = default);

    /// <summary>Is the table open for booking in this interval (working hours, blackout dates)? Existing bookings are NOT considered here.</summary>
    Task<bool> IsTableOpenAsync(Guid tableId, DateTime startTime, DateTime endTime, CancellationToken ct = default);
}

// ---------- Redis (later: Redis implementation) ----------

public interface IIdempotencyStore
{
    /// <summary>true = the key is new and now belongs to this request; false = another request with this key is in progress.</summary>
    Task<bool> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default);

    Task ReleaseAsync(string key, CancellationToken ct = default);
}

public interface ITableLockStore
{
    /// <summary>
    /// Atomically locks ALL the slots of the table for the owner (all or nothing).
    /// A slot already locked by the same owner does not count as a conflict.
    /// </summary>
    Task<bool> TryLockSlotsAsync(Guid tableId, IReadOnlyCollection<DateTime> slots, Guid ownerId,
        TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Releases only those locks that belong to the owner.</summary>
    Task ReleaseSlotsAsync(Guid tableId, IReadOnlyCollection<DateTime> slots, Guid ownerId,
        CancellationToken ct = default);
}

// ---------- RabbitMQ (later: RabbitMQ implementation) ----------

public interface IMessagePublisher
{
    Task PublishAsync<T>(T message, CancellationToken ct = default) where T : class;
}

// ---------- SignalR (later: SignalR implementation) ----------

public interface ITableNotifier
{
    Task TableLockedAsync(Guid tableId, DateTime slotTime, CancellationToken ct = default);
    Task TableReleasedAsync(Guid tableId, DateTime slotTime, CancellationToken ct = default);
    Task BookingStatusChangedAsync(Guid bookingId, BookingStatus status, CancellationToken ct = default);
}
