using Microsoft.EntityFrameworkCore;

namespace AvailabilityServiceApi;

public class SlotService
{
    private readonly CatalogDbContext _db;
    private readonly BlackoutDateService _blackouts;
    private readonly IOccupancyProvider _occupancy;
    private readonly IDateTimeProvider _clock;

    public SlotService(CatalogDbContext db, BlackoutDateService blackouts,
        IOccupancyProvider occupancy, IDateTimeProvider clock)
    {
        _db = db;
        _blackouts = blackouts;
        _occupancy = occupancy;
        _clock = clock;
    }

    // ---------------------------------------------------------------- slot generation

    /// <summary>Generates slots for the next N days (called by SlotGenerationWorker).</summary>
    public async Task<int> GenerateAheadAsync(int daysAhead, CancellationToken ct = default)
    {
        var today = _clock.UtcNow.Date;
        var created = 0;
        for (var i = 0; i < daysAhead; i++)
            created += await EnsureSlotsForDateAsync(today.AddDays(i), ct);
        return created;
    }

    /// <summary>Creates the time grid for one date if it does not exist yet. Returns how many slots were created.</summary>
    public async Task<int> EnsureSlotsForDateAsync(DateTime date, CancellationToken ct = default)
    {
        date = date.Date;
        if (date < _clock.UtcNow.Date) return 0; // never generate slots in the past

        var dayOfWeek = (int)date.DayOfWeek;
        var hours = await _db.WorkingHours.FirstOrDefaultAsync(w => w.DayOfWeek == dayOfWeek, ct);
        if (hours is null || hours.SlotStepMinutes <= 0) return 0; // closed that day

        if (await _db.TimeSlots.AnyAsync(s => s.WorkingHoursId == hours.Id && s.SlotDate == date, ct))
            return 0;

        var step = TimeSpan.FromMinutes(hours.SlotStepMinutes);
        var slots = new List<TimeSlot>();
        for (var t = hours.OpenTime; t < hours.CloseTime; t += step)
        {
            slots.Add(new TimeSlot
            {
                Id = Guid.NewGuid(),
                WorkingHoursId = hours.Id,
                SlotDate = date,
                SlotTime = t,
                IsAvailable = true
            });
        }

        _db.TimeSlots.AddRange(slots);
        try
        {
            await _db.SaveChangesAsync(ct);
            return slots.Count;
        }
        catch (DbUpdateException)
        {
            // someone generated the same slots at the same moment (unique index) - that is fine
            foreach (var slot in slots) _db.Entry(slot).State = EntityState.Detached;
            return 0;
        }
    }

    // ---------------------------------------------------------------- queries

    /// <summary>GET /api/halls/{hallId}/slots: slots x tables of the hall with the IsAvailable flag.</summary>
    public async Task<List<SlotResponse>> GetFreeSlotsAsync(
        Guid hallId, FreeSlotsRequest request, CancellationToken ct = default)
    {
        if (!await _db.Halls.AnyAsync(h => h.Id == hallId, ct))
            throw new ServiceException(404, "Hall not found");

        var date = (request.Date ?? _clock.UtcNow).Date;

        var query = _db.Tables.AsNoTracking().Where(t => t.HallId == hallId);
        if (request.GuestsCount is int guests)
            query = query.Where(t => t.Capacity >= guests);
        if (!string.IsNullOrWhiteSpace(request.TableType))
        {
            if (!TableTypes.TryParse(request.TableType, out var type))
                throw new ServiceException(400, "Unknown table type. Allowed: Standard, Vip, Bar");
            query = query.Where(t => t.TableType == type);
        }
        var tables = await query.OrderBy(t => t.TableNumber).ToListAsync(ct);

        var cells = await ComputeAsync(date, tables, request.Time, ct);
        return cells
            .OrderBy(c => c.Time).ThenBy(c => c.Table.TableNumber)
            .Select(c => new SlotResponse
            {
                SlotTime = c.Time,
                TableId = c.Table.Id,
                TableNumber = c.Table.TableNumber,
                Capacity = c.Table.Capacity,
                IsAvailable = c.Free
            })
            .ToList();
    }

    /// <summary>GET /api/tables/{tableId}/availability: all slots of one table on one date.</summary>
    public async Task<TableAvailabilityResponse> GetTableAvailabilityAsync(
        Guid tableId, DateTime? date, CancellationToken ct = default)
    {
        var table = await _db.Tables.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tableId, ct)
                    ?? throw new ServiceException(404, "Table not found");

        var day = (date ?? _clock.UtcNow).Date;
        var cells = await ComputeAsync(day, new List<RestaurantTable> { table }, null, ct);

        return new TableAvailabilityResponse
        {
            TableId = tableId,
            Date = day,
            Slots = cells.Select(c => new SlotAvailabilityResponse
            {
                SlotTime = c.Time,
                IsAvailable = c.Free
            }).ToList()
        };
    }

    /// <summary>Will be used by the gRPC method GetTableInfo (BookingService asks for it).</summary>
    public async Task<TableInfoDto> GetTableInfoAsync(Guid tableId, CancellationToken ct = default)
    {
        var table = await _db.Tables.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tableId, ct)
                    ?? throw new ServiceException(404, "Table not found");
        return new TableInfoDto(table.Id, table.HallId, table.Capacity, table.TableType.ToString());
    }

    /// <summary>
    /// Used by the gRPC method GetBookingParameters: the slot step of that weekday, the stay duration
    /// and the cleaning buffer. null = the restaurant does not work that day.
    /// </summary>
    public async Task<BookingParametersDto?> GetBookingParametersAsync(DateTime date, CancellationToken ct = default)
    {
        var dayOfWeek = (int)date.Date.DayOfWeek;
        var hours = await _db.WorkingHours.AsNoTracking().FirstOrDefaultAsync(w => w.DayOfWeek == dayOfWeek, ct);
        if (hours is null) return null;

        var duration = await GetDurationAsync(ct);
        return new BookingParametersDto(hours.SlotStepMinutes, duration.DefaultDurationMinutes, duration.CleaningBufferMinutes);
    }

    private async Task<BookingDuration> GetDurationAsync(CancellationToken ct)
        => await _db.BookingDurations.AsNoTracking().FirstOrDefaultAsync(ct)
           ?? new BookingDuration { DefaultDurationMinutes = 120, CleaningBufferMinutes = 15 };

    // ---------------------------------------------------------------- the core calculation

    private record Cell(RestaurantTable Table, TimeSpan Time, bool Free);

    /// <summary>
    /// For every (slot, table) decides whether a booking that starts at that slot is possible.
    /// A slot is NOT free if: the slot is closed, the date is a blackout date for the hall,
    /// the stay would end after closing time, or it overlaps an existing booking of the table
    /// (booking duration + cleaning buffer).
    /// </summary>
    private async Task<List<Cell>> ComputeAsync(
        DateTime date, List<RestaurantTable> tables, TimeSpan? onlyTime, CancellationToken ct)
    {
        var result = new List<Cell>();
        if (tables.Count == 0) return result;

        await EnsureSlotsForDateAsync(date, ct);

        var dayOfWeek = (int)date.DayOfWeek;
        var hours = await _db.WorkingHours.AsNoTracking().FirstOrDefaultAsync(w => w.DayOfWeek == dayOfWeek, ct);
        if (hours is null) return result; // the restaurant is closed that day

        var slotsQuery = _db.TimeSlots.AsNoTracking()
            .Where(s => s.WorkingHoursId == hours.Id && s.SlotDate == date);
        if (onlyTime is TimeSpan only)
            slotsQuery = slotsQuery.Where(s => s.SlotTime == only);
        var slots = await slotsQuery.OrderBy(s => s.SlotTime).ToListAsync(ct);

        var duration = await GetDurationAsync(ct);
        var stay = TimeSpan.FromMinutes(duration.DefaultDurationMinutes);
        var window = stay + TimeSpan.FromMinutes(duration.CleaningBufferMinutes); // how long the table is "busy"

        var blocked = new Dictionary<Guid, bool>();
        foreach (var hallId in tables.Select(t => t.HallId).Distinct())
            blocked[hallId] = await _blackouts.IsBlackoutAsync(date, hallId, ct);

        var tableIds = tables.Select(t => t.Id).ToList();
        var bookingStarts = (await _occupancy.GetBookingStartsAsync(date, tableIds, ct))
            .ToLookup(b => b.TableId, b => b.Start);

        foreach (var slot in slots)
        {
            var slotStart = date + slot.SlotTime;
            var slotEnd = slotStart + window;
            var fitsBeforeClosing = slot.SlotTime + stay <= hours.CloseTime;

            foreach (var table in tables)
            {
                var overlaps = bookingStarts[table.Id]
                    .Any(bookingStart => bookingStart < slotEnd && bookingStart + window > slotStart);
                var free = slot.IsAvailable && !blocked[table.HallId] && fitsBeforeClosing && !overlaps;
                result.Add(new Cell(table, slot.SlotTime, free));
            }
        }
        return result;
    }
}

public record BookingParametersDto(int SlotStepMinutes, int DurationMinutes, int CleaningBufferMinutes);
