using System.Globalization;
using System.Text.Json;

namespace BookingServiceApi;

// Temporary implementations for stages 1-4. They live in memory / write to the log.
// All of them are registered as singletons in Program.cs (they keep state).

public class FakeAuthClient : IAuthClient
{
    public Task<UserContact?> GetUserContactAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult<UserContact?>(
            new UserContact(userId, "Test User", "+79990000000", "test@mail.com"));
}

/// <summary>Pretends to be AvailabilityService: every table (except Guid.Empty) exists, seats 10, the restaurant works 12:00-23:00.</summary>
public class FakeAvailabilityClient : IAvailabilityClient
{
    public Task<TableInfo?> GetTableInfoAsync(Guid tableId, CancellationToken ct = default)
        => Task.FromResult<TableInfo?>(
            tableId == Guid.Empty ? null : new TableInfo(tableId, Guid.Empty, 10, "Standard"));

    public Task<BookingParameters> GetBookingParametersAsync(DateTime date, CancellationToken ct = default)
        => Task.FromResult(new BookingParameters(SlotStepMinutes: 30, DurationMinutes: 120, CleaningBufferMinutes: 15));

    public Task<bool> IsTableOpenAsync(Guid tableId, DateTime startTime, DateTime endTime, CancellationToken ct = default)
    {
        var open = startTime.Date == endTime.Date
                   && startTime.TimeOfDay >= TimeSpan.FromHours(12)
                   && endTime.TimeOfDay <= TimeSpan.FromHours(23);
        return Task.FromResult(open);
    }
}

public class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly Dictionary<string, DateTime> _keys = new();
    private readonly object _gate = new();

    public Task<bool> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            if (_keys.TryGetValue(key, out var expires) && expires > now)
                return Task.FromResult(false);

            _keys[key] = now + ttl;
            return Task.FromResult(true);
        }
    }

    public Task ReleaseAsync(string key, CancellationToken ct = default)
    {
        lock (_gate) _keys.Remove(key);
        return Task.CompletedTask;
    }
}

/// <summary>Imitates Redis keys lock:table:{table_id}:slot:{slot_datetime} with the "set if not exists" semantics.</summary>
public class InMemoryTableLockStore : ITableLockStore
{
    private readonly Dictionary<string, (Guid Owner, DateTime Expires)> _locks = new();
    private readonly object _gate = new();

    private static string Key(Guid tableId, DateTime slot)
        => $"lock:table:{tableId}:slot:{slot.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture)}";

    public Task<bool> TryLockSlotsAsync(Guid tableId, IReadOnlyCollection<DateTime> slots, Guid ownerId,
        TimeSpan ttl, CancellationToken ct = default)
    {
        lock (_gate) // the whole check+set is one atomic step: all or nothing
        {
            var now = DateTime.UtcNow;
            var keys = slots.Select(s => Key(tableId, s)).ToList();

            foreach (var key in keys)
            {
                if (_locks.TryGetValue(key, out var existing) && existing.Expires > now && existing.Owner != ownerId)
                    return Task.FromResult(false);
            }

            foreach (var key in keys)
                _locks[key] = (ownerId, now + ttl);
            return Task.FromResult(true);
        }
    }

    public Task ReleaseSlotsAsync(Guid tableId, IReadOnlyCollection<DateTime> slots, Guid ownerId,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            foreach (var slot in slots)
            {
                var key = Key(tableId, slot);
                if (_locks.TryGetValue(key, out var existing) && existing.Owner == ownerId)
                    _locks.Remove(key);
            }
        }
        return Task.CompletedTask;
    }
}

public class LoggingMessagePublisher : IMessagePublisher
{
    private readonly ILogger<LoggingMessagePublisher> _logger;

    public LoggingMessagePublisher(ILogger<LoggingMessagePublisher> logger) => _logger = logger;

    public Task PublishAsync<T>(T message, CancellationToken ct = default) where T : class
    {
        _logger.LogInformation("[fake RabbitMQ] {Type}: {Json}",
            message.GetType().Name, JsonSerializer.Serialize(message, message.GetType()));
        return Task.CompletedTask;
    }
}

public class LoggingTableNotifier : ITableNotifier
{
    private readonly ILogger<LoggingTableNotifier> _logger;

    public LoggingTableNotifier(ILogger<LoggingTableNotifier> logger) => _logger = logger;

    public Task TableLockedAsync(Guid tableId, DateTime slotTime, CancellationToken ct = default)
    {
        _logger.LogInformation("[fake SignalR] TableLocked {TableId} {Slot:HH:mm}", tableId, slotTime);
        return Task.CompletedTask;
    }

    public Task TableReleasedAsync(Guid tableId, DateTime slotTime, CancellationToken ct = default)
    {
        _logger.LogInformation("[fake SignalR] TableReleased {TableId} {Slot:HH:mm}", tableId, slotTime);
        return Task.CompletedTask;
    }

    public Task BookingStatusChangedAsync(Guid bookingId, BookingStatus status, CancellationToken ct = default)
    {
        _logger.LogInformation("[fake SignalR] BookingStatusChanged {BookingId} -> {Status}", bookingId, status);
        return Task.CompletedTask;
    }
}
