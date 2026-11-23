using System.Globalization;
using StackExchange.Redis;

namespace BookingServiceApi;

// The real Redis implementations of IIdempotencyStore and ITableLockStore.
// The keys are the same as in the in-memory fakes:
//   idem:{key}                              - the idempotency key of a booking request
//   lock:table:{table_id}:slot:{yyyy-MM-ddTHH:mm} - the lock of one slot of a table (the value = the owner, i.e. the booking id)

/// <summary>Turns "Redis is down" into a clean 503 instead of an unexplained 500.</summary>
internal static class RedisGuard
{
    public static async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (RedisException)
        {
            throw new ServiceException(503, "Redis is unavailable");
        }
    }
}

public class RedisIdempotencyStore : IIdempotencyStore
{
    private readonly IConnectionMultiplexer _redis;

    public RedisIdempotencyStore(IConnectionMultiplexer redis) => _redis = redis;

    public Task<bool> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default)
        // SET idem:{key} 1 NX PX ttl  -> true only for the first caller
        => RedisGuard.RunAsync(() => _redis.GetDatabase().StringSetAsync($"idem:{key}", "1", ttl, When.NotExists));

    public Task ReleaseAsync(string key, CancellationToken ct = default)
        => RedisGuard.RunAsync(() => _redis.GetDatabase().KeyDeleteAsync($"idem:{key}"));
}

public class RedisTableLockStore : ITableLockStore
{
    // All or nothing, atomically: Redis runs a Lua script as one indivisible step,
    // so two requests can never lock the same slot at the same time.
    // KEYS = the lock keys of all slots, ARGV[1] = the owner, ARGV[2] = ttl in milliseconds.
    private const string LockScript = @"
for i = 1, #KEYS do
  local current = redis.call('GET', KEYS[i])
  if current and current ~= ARGV[1] then
    return 0
  end
end
for i = 1, #KEYS do
  redis.call('SET', KEYS[i], ARGV[1], 'PX', ARGV[2])
end
return 1";

    // Deletes only the locks that belong to the owner.
    private const string ReleaseScript = @"
for i = 1, #KEYS do
  if redis.call('GET', KEYS[i]) == ARGV[1] then
    redis.call('DEL', KEYS[i])
  end
end
return 1";

    private readonly IConnectionMultiplexer _redis;

    public RedisTableLockStore(IConnectionMultiplexer redis) => _redis = redis;

    private static RedisKey[] Keys(Guid tableId, IReadOnlyCollection<DateTime> slots)
        => slots.Select(s => (RedisKey)$"lock:table:{tableId}:slot:{s.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture)}")
                .ToArray();

    public Task<bool> TryLockSlotsAsync(Guid tableId, IReadOnlyCollection<DateTime> slots, Guid ownerId,
        TimeSpan ttl, CancellationToken ct = default)
    {
        if (slots.Count == 0)
            return Task.FromResult(true);

        return RedisGuard.RunAsync(async () =>
        {
            var result = await _redis.GetDatabase().ScriptEvaluateAsync(
                LockScript,
                Keys(tableId, slots),
                new RedisValue[] { ownerId.ToString(), (long)ttl.TotalMilliseconds });
            return (int)result == 1;
        });
    }

    public async Task ReleaseSlotsAsync(Guid tableId, IReadOnlyCollection<DateTime> slots, Guid ownerId,
        CancellationToken ct = default)
    {
        if (slots.Count == 0)
            return;

        await RedisGuard.RunAsync(async () =>
        {
            await _redis.GetDatabase().ScriptEvaluateAsync(
                ReleaseScript,
                Keys(tableId, slots),
                new RedisValue[] { ownerId.ToString() });
            return true;
        });
    }
}
