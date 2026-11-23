using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BookingServiceApi;

public class BookingService
{
    private readonly BookingDbContext _db;
    private readonly IAuthClient _auth;
    private readonly IAvailabilityClient _availability;
    private readonly IIdempotencyStore _idempotency;
    private readonly ITableLockStore _locks;
    private readonly IMessagePublisher _publisher;
    private readonly ITableNotifier _notifier;
    private readonly IDateTimeProvider _clock;
    private readonly BookingSettings _settings;
    private readonly ILogger<BookingService> _logger;

    public BookingService(BookingDbContext db, IAuthClient auth, IAvailabilityClient availability,
        IIdempotencyStore idempotency, ITableLockStore locks, IMessagePublisher publisher,
        ITableNotifier notifier, IDateTimeProvider clock, IOptions<BookingSettings> settings,
        ILogger<BookingService> logger)
    {
        _db = db;
        _auth = auth;
        _availability = availability;
        _idempotency = idempotency;
        _locks = locks;
        _publisher = publisher;
        _notifier = notifier;
        _clock = clock;
        _settings = settings.Value;
        _logger = logger;
    }

    // =================================================================== create

    public async Task<CreateBookingResult> CreateAsync(
        Guid userId, CreateBookingRequest request, CancellationToken ct = default)
    {
        if (request.TableId == Guid.Empty)
            throw new ServiceException(400, "TableId is required");

        var key = request.IdempotencyKey.Trim();

        // 1. A repeated request (double click, retry): return the booking that already exists.
        var existing = await _db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            if (existing.UserId != userId)
                throw new ServiceException(409, "This idempotency key is already used");
            return new CreateBookingResult(Map(existing), false);
        }

        // 2. Validation
        var start = NormalizeTime(request.StartTime);
        if (start <= _clock.UtcNow)
            throw new ServiceException(400, "Start time is in the past");

        var contact = await _auth.GetUserContactAsync(userId, ct)
                      ?? throw new ServiceException(401, "User not found");

        var table = await _availability.GetTableInfoAsync(request.TableId, ct)
                    ?? throw new ServiceException(404, "Table not found");
        if (request.GuestsCount > table.Capacity)
            throw new ServiceException(400, $"The table seats only {table.Capacity} guests");

        var parameters = await _availability.GetBookingParametersAsync(start.Date, ct);
        var plan = BuildPlan(start, parameters);

        if (!await _availability.IsTableOpenAsync(table.TableId, start, plan.End, ct))
            throw new ServiceException(409, "The table is not available at this time (closed or blackout date)");

        // 3. Protection from two simultaneous requests with the same key
        if (!await _idempotency.TryAcquireAsync(key, TimeSpan.FromMinutes(10), ct))
            throw new ServiceException(409, "A request with this idempotency key is already being processed");

        var bookingId = Guid.NewGuid();
        var locked = false;
        Booking booking;
        try
        {
            // 4. Lock all the slots (all or nothing)
            locked = await _locks.TryLockSlotsAsync(table.TableId, plan.Slots, bookingId, LockTtl(plan.BusyUntil), ct);
            if (!locked)
                throw new ServiceException(409, "The table is already booked or locked for this time");

            // 5. One transaction: the booking + all its slots + history + reminder
            var now = _clock.UtcNow;
            booking = new Booking
            {
                Id = bookingId,
                UserId = userId,
                TableId = table.TableId,
                GuestName = string.IsNullOrWhiteSpace(request.GuestName) ? contact.FullName : request.GuestName.Trim(),
                GuestPhone = string.IsNullOrWhiteSpace(request.GuestPhone) ? contact.Phone : request.GuestPhone.Trim(),
                GuestEmail = string.IsNullOrWhiteSpace(request.GuestEmail) ? contact.Email : request.GuestEmail.Trim(),
                GuestCount = request.GuestsCount,
                StartTime = start,
                EndTime = plan.End,
                Status = BookingStatus.Pending,
                IdempotencyKey = key,
                ConfirmationCode = GenerateConfirmationCode(),
                CreationDate = now,
                Slots = plan.Slots.Select(s => new BookingSlot
                {
                    Id = Guid.NewGuid(),
                    TableId = table.TableId,
                    SlotDateTime = s
                }).ToList()
            };
            booking.History.Add(new BookingStatusHistory
            {
                Id = Guid.NewGuid(),
                Status = BookingStatus.Pending,
                ChangedAt = now,
                ChangedBy = userId
            });

            var remindAt = start.AddHours(-_settings.ReminderHoursBefore);
            if (remindAt > now)
                booking.Reminders.Add(new Reminder { Id = Guid.NewGuid(), RemindAt = remindAt, IsSent = false });

            _db.Bookings.Add(booking);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // the unique index (table_id, slot_datetime) is the second line of defence
                throw new ServiceException(409, "The table is already booked for this time");
            }
        }
        catch
        {
            // saving failed: remove the locks and let the client retry with the same key
            if (locked)
                await SafeAsync(() => _locks.ReleaseSlotsAsync(table.TableId, plan.Slots, bookingId, CancellationToken.None), "release locks");
            await SafeAsync(() => _idempotency.ReleaseAsync(key, CancellationToken.None), "release idempotency key");
            throw;
        }

        // 6. Events. A failure here must not cancel the booking that is already saved.
        await SafeAsync(() => _publisher.PublishAsync(new BookingCreatedEvent(
            booking.Id, booking.UserId, booking.GuestName, booking.GuestPhone, booking.GuestEmail,
            booking.TableId, booking.StartTime, booking.EndTime, booking.GuestCount,
            booking.ConfirmationCode), ct), "publish BookingCreated");
        foreach (var slot in plan.Slots)
        {
            var s = slot;
            await SafeAsync(() => _notifier.TableLockedAsync(table.TableId, s, ct), "notify TableLocked");
        }

        return new CreateBookingResult(Map(booking), true);
    }

    // =================================================================== read

    public async Task<List<BookingResponse>> GetMyAsync(Guid userId, CancellationToken ct = default)
    {
        var bookings = await _db.Bookings.AsNoTracking()
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.StartTime)
            .ToListAsync(ct);
        return bookings.Select(Map).ToList();
    }

    public async Task<BookingResponse> GetByIdAsync(Guid bookingId, Guid userId, CancellationToken ct = default)
    {
        var booking = await _db.Bookings.AsNoTracking()
                          .FirstOrDefaultAsync(b => b.Id == bookingId && b.UserId == userId, ct)
                      ?? throw new ServiceException(404, "Booking not found");
        return Map(booking);
    }

    // =================================================================== update

    /// <summary>Changes the table, the time or the number of guests of a Pending/Confirmed booking.</summary>
    public async Task<BookingResponse> UpdateAsync(Guid bookingId, Guid actorId, bool isStaff,
        UpdateBookingRequest request, CancellationToken ct = default)
    {
        var booking = await _db.Bookings.Include(b => b.Slots).FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null || (!isStaff && booking.UserId != actorId))
            throw new ServiceException(404, "Booking not found");
        if (booking.Status is not (BookingStatus.Pending or BookingStatus.Confirmed))
            throw new ServiceException(409, "Only pending or confirmed bookings can be changed");
        if (request.TableId == Guid.Empty)
            throw new ServiceException(400, "TableId is required");

        var start = NormalizeTime(request.StartTime);
        if (start <= _clock.UtcNow)
            throw new ServiceException(400, "Start time is in the past");

        var table = await _availability.GetTableInfoAsync(request.TableId, ct)
                    ?? throw new ServiceException(404, "Table not found");
        if (request.GuestsCount > table.Capacity)
            throw new ServiceException(400, $"The table seats only {table.Capacity} guests");

        var parameters = await _availability.GetBookingParametersAsync(start.Date, ct);
        var plan = BuildPlan(start, parameters);
        if (!await _availability.IsTableOpenAsync(table.TableId, start, plan.End, ct))
            throw new ServiceException(409, "The table is not available at this time (closed or blackout date)");

        var oldTableId = booking.TableId;
        var oldSlots = booking.Slots.Select(s => s.SlotDateTime).OrderBy(s => s).ToList();
        var samePlace = oldTableId == table.TableId && oldSlots.SequenceEqual(plan.Slots);

        if (samePlace)
        {
            booking.GuestCount = request.GuestsCount; // only the number of guests changes
            await _db.SaveChangesAsync(ct);
            return Map(booking);
        }

        // Lock the new slots. The slots that already belong to this booking are not a conflict.
        if (!await _locks.TryLockSlotsAsync(table.TableId, plan.Slots, booking.Id, LockTtl(plan.BusyUntil), ct))
            throw new ServiceException(409, "The table is already booked or locked for this time");

        // the locks that were taken just now (the others were already ours)
        var addedLocks = oldTableId == table.TableId ? plan.Slots.Except(oldSlots).ToList() : plan.Slots;

        try
        {
            // two steps in one transaction: first delete the old slots, then insert the new ones
            // (otherwise the unique index may complain about slots that stay the same)
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            _db.BookingSlots.RemoveRange(booking.Slots.ToList());
            await _db.SaveChangesAsync(ct);

            foreach (var slot in plan.Slots)
            {
                _db.BookingSlots.Add(new BookingSlot
                {
                    Id = Guid.NewGuid(),
                    BookingId = booking.Id,
                    TableId = table.TableId,
                    SlotDateTime = slot
                });
            }
            booking.TableId = table.TableId;
            booking.StartTime = start;
            booking.EndTime = plan.End;
            booking.GuestCount = request.GuestsCount;
            await _db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await SafeAsync(() => _locks.ReleaseSlotsAsync(table.TableId, addedLocks, booking.Id, CancellationToken.None), "release locks");
            throw new ServiceException(409, "The table is already booked for this time");
        }
        catch
        {
            await SafeAsync(() => _locks.ReleaseSlotsAsync(table.TableId, addedLocks, booking.Id, CancellationToken.None), "release locks");
            throw;
        }

        // free what is not needed any more
        var releasedSlots = oldTableId == table.TableId ? oldSlots.Except(plan.Slots).ToList() : oldSlots;
        await SafeAsync(() => _locks.ReleaseSlotsAsync(oldTableId, releasedSlots, booking.Id, ct), "release old locks");
        foreach (var slot in releasedSlots)
        {
            var s = slot;
            await SafeAsync(() => _notifier.TableReleasedAsync(oldTableId, s, ct), "notify TableReleased");
        }
        foreach (var slot in addedLocks)
        {
            var s = slot;
            await SafeAsync(() => _notifier.TableLockedAsync(table.TableId, s, ct), "notify TableLocked");
        }

        return Map(booking);
    }

    // =================================================================== status changes

    /// <summary>Cancellation by the guest (own booking) or by the staff.</summary>
    public async Task CancelAsync(Guid bookingId, Guid actorId, bool isStaff, CancellationToken ct = default)
    {
        var booking = await _db.Bookings.Include(b => b.Slots).FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null || (!isStaff && booking.UserId != actorId))
            throw new ServiceException(404, "Booking not found");
        if (booking.Status is not (BookingStatus.Pending or BookingStatus.Confirmed))
            throw new ServiceException(409, "The booking is already cancelled or finished");

        await ReleaseBookingAsync(booking, actorId, "Cancelled", ct);
    }

    /// <summary>Pending -> Confirmed. Later it is also called by NotificationResultConsumer (changedBy = Guid.Empty).</summary>
    public Task<BookingResponse> ConfirmAsync(Guid bookingId, Guid changedBy, CancellationToken ct = default)
        => TransitionAsync(bookingId, changedBy, BookingStatus.Confirmed,
            new[] { BookingStatus.Pending }, "The booking is already confirmed or cancelled", ct);

    /// <summary>Confirmed -> Completed.</summary>
    public Task<BookingResponse> CompleteAsync(Guid bookingId, Guid changedBy, CancellationToken ct = default)
        => TransitionAsync(bookingId, changedBy, BookingStatus.Completed,
            new[] { BookingStatus.Confirmed }, "Only a confirmed booking can be completed", ct);

    /// <summary>Confirmed -> NoShow.</summary>
    public Task<BookingResponse> NoShowAsync(Guid bookingId, Guid changedBy, CancellationToken ct = default)
        => TransitionAsync(bookingId, changedBy, BookingStatus.NoShow,
            new[] { BookingStatus.Confirmed }, "Only a confirmed booking can be marked as no-show", ct);

    private async Task<BookingResponse> TransitionAsync(Guid bookingId, Guid changedBy, BookingStatus to,
        BookingStatus[] allowedFrom, string conflictMessage, CancellationToken ct)
    {
        var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId, ct)
                      ?? throw new ServiceException(404, "Booking not found");
        if (!allowedFrom.Contains(booking.Status))
            throw new ServiceException(409, conflictMessage);

        SetStatus(booking, to, changedBy);
        await _db.SaveChangesAsync(ct);

        await SafeAsync(() => _notifier.BookingStatusChangedAsync(booking.Id, to, ct), "notify BookingStatusChanged");
        return Map(booking);
    }

    /// <summary>
    /// The ONE place where a booking gets released (cancellation by the guest, by the staff, or expiration):
    /// the slots are deleted, the locks are removed, status = Cancelled, the event is published, clients are notified.
    /// </summary>
    private async Task ReleaseBookingAsync(Booking booking, Guid changedBy, string reason, CancellationToken ct)
    {
        var slots = booking.Slots.Select(s => s.SlotDateTime).ToList();
        var tableId = booking.TableId;

        _db.BookingSlots.RemoveRange(booking.Slots.ToList());
        SetStatus(booking, BookingStatus.Cancelled, changedBy);
        await _db.SaveChangesAsync(ct);

        // nobody needs the reminders of a cancelled booking
        await _db.Reminders.Where(r => r.BookingId == booking.Id && !r.IsSent).ExecuteDeleteAsync(ct);

        await SafeAsync(() => _locks.ReleaseSlotsAsync(tableId, slots, booking.Id, ct), "release locks");
        await SafeAsync(() => _publisher.PublishAsync(new BookingCancelledEvent(
            booking.Id, booking.GuestName, booking.GuestPhone, booking.GuestEmail,
            booking.StartTime, reason), ct), "publish BookingCancelled");
        foreach (var slot in slots)
        {
            var s = slot;
            await SafeAsync(() => _notifier.TableReleasedAsync(tableId, s, ct), "notify TableReleased");
        }
        await SafeAsync(() => _notifier.BookingStatusChangedAsync(booking.Id, BookingStatus.Cancelled, ct),
            "notify BookingStatusChanged");
    }

    /// <summary>Cancels the Pending bookings that nobody confirmed in time (called by the background worker).</summary>
    public async Task<int> ExpirePendingAsync(CancellationToken ct = default)
    {
        var threshold = _clock.UtcNow.AddMinutes(-_settings.PendingTimeoutMinutes);
        var expired = await _db.Bookings.Include(b => b.Slots)
            .Where(b => b.Status == BookingStatus.Pending && b.CreationDate < threshold)
            .Take(100)
            .ToListAsync(ct);

        var count = 0;
        foreach (var booking in expired)
        {
            try
            {
                await ReleaseBookingAsync(booking, Guid.Empty, "Expired", ct);
                count++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Could not expire booking {BookingId}", booking.Id);
            }
        }
        return count;
    }

    /// <summary>
    /// Publishes BookingReminder for the reminders that are due (called by BookingReminderWorker).
    /// Only Confirmed bookings whose visit has not started yet get a reminder.
    /// If the publish fails, the reminder stays unsent and is tried again at the next run.
    /// </summary>
    public async Task<int> SendDueRemindersAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var due = await _db.Reminders.Include(r => r.Booking)
            .Where(r => !r.IsSent && r.RemindAt <= now
                        && r.Booking.Status == BookingStatus.Confirmed
                        && r.Booking.StartTime > now)
            .OrderBy(r => r.RemindAt)
            .Take(100)
            .ToListAsync(ct);

        var count = 0;
        foreach (var reminder in due)
        {
            var b = reminder.Booking;
            try
            {
                await _publisher.PublishAsync(new BookingReminderEvent(
                    b.Id, b.GuestName, b.GuestPhone, b.GuestEmail, b.StartTime, b.GuestCount), ct);
                reminder.IsSent = true;
                await _db.SaveChangesAsync(ct);
                count++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // IsSent stays false: the next run will try again
                _logger.LogError(ex, "Could not publish the reminder of booking {BookingId}", b.Id);
            }
        }
        return count;
    }

    // =================================================================== helpers

    private void SetStatus(Booking booking, BookingStatus status, Guid changedBy)
    {
        booking.Status = status;
        _db.StatusHistory.Add(new BookingStatusHistory
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            Status = status,
            ChangedAt = _clock.UtcNow,
            ChangedBy = changedBy
        });
    }

    private record Plan(DateTime End, List<DateTime> Slots, DateTime BusyUntil);

    /// <summary>
    /// Start 19:00, stay 120 min, cleaning 15 min, step 30 min:
    /// End = 21:00, slots = 19:00, 19:30, 20:00, 20:30, 21:00 (everything before 21:15), BusyUntil = 21:15.
    /// This matches how AvailabilityService counts the busy window.
    /// </summary>
    private static Plan BuildPlan(DateTime start, BookingParameters p)
    {
        if (p.SlotStepMinutes <= 0 || p.DurationMinutes <= 0)
            throw new ServiceException(503, "Booking parameters are not configured");

        var step = TimeSpan.FromMinutes(p.SlotStepMinutes);
        if (start.TimeOfDay.Ticks % step.Ticks != 0)
            throw new ServiceException(400,
                $"Start time must be a multiple of {p.SlotStepMinutes} minutes (for example 19:00 or 19:30)");

        var end = start.AddMinutes(p.DurationMinutes);
        var busyUntil = end.AddMinutes(p.CleaningBufferMinutes);

        var slots = new List<DateTime>();
        for (var t = start; t < busyUntil; t += step)
            slots.Add(t);

        return new Plan(end, slots, busyUntil);
    }

    /// <summary>Times are treated as the restaurant's local time without a time zone.</summary>
    private static DateTime NormalizeTime(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    private TimeSpan LockTtl(DateTime busyUntil)
    {
        var ttl = busyUntil - _clock.UtcNow;
        return ttl < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : ttl;
    }

    private static string GenerateConfirmationCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O, 1/I
        var chars = new char[8];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return new string(chars);
    }

    private async Task SafeAsync(Func<Task> action, string what)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Side effect failed: {What}", what);
        }
    }

    private static BookingResponse Map(Booking b) => new()
    {
        Id = b.Id,
        TableId = b.TableId,
        StartTime = b.StartTime,
        EndTime = b.EndTime,
        GuestsCount = b.GuestCount,
        Status = b.Status.ToString(),
        CreationDate = b.CreationDate,
        ConfirmationCode = b.ConfirmationCode
    };
}
