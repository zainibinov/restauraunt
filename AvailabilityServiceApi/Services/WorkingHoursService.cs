using Microsoft.EntityFrameworkCore;

namespace AvailabilityServiceApi;

public class WorkingHoursService
{
    private readonly CatalogDbContext _db;
    private readonly IDateTimeProvider _clock;

    public WorkingHoursService(CatalogDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<List<WorkingHoursResponse>> GetAllAsync()
    {
        var hours = await _db.WorkingHours.AsNoTracking().OrderBy(w => w.DayOfWeek).ToListAsync();
        return hours.Select(Map).ToList();
    }

    /// <summary>Sets the hours of one weekday (creates the row if it does not exist yet).</summary>
    public async Task<WorkingHoursResponse> UpdateAsync(WorkingHoursRequest request)
    {
        if (request.OpenTime < TimeSpan.Zero || request.CloseTime > TimeSpan.FromHours(24)
            || request.OpenTime >= request.CloseTime)
            throw new ServiceException(400, "OpenTime must be earlier than CloseTime (00:00-24:00)");

        var hours = await _db.WorkingHours.FirstOrDefaultAsync(w => w.DayOfWeek == request.DayOfWeek);
        if (hours is null)
        {
            hours = new WorkingHours { Id = Guid.NewGuid(), DayOfWeek = request.DayOfWeek };
            _db.WorkingHours.Add(hours);
        }

        hours.OpenTime = request.OpenTime;
        hours.CloseTime = request.CloseTime;
        hours.SlotStepMinutes = request.SlotStepMinutes;
        await _db.SaveChangesAsync();

        // the already generated future slots are out of date: delete them,
        // SlotService will generate them again with the new hours
        var today = _clock.UtcNow.Date;
        await _db.TimeSlots
            .Where(s => s.WorkingHoursId == hours.Id && s.SlotDate >= today)
            .ExecuteDeleteAsync();

        return Map(hours);
    }

    private static WorkingHoursResponse Map(WorkingHours w) => new()
    {
        Id = w.Id,
        DayOfWeek = w.DayOfWeek,
        OpenTime = w.OpenTime,
        CloseTime = w.CloseTime,
        SlotStepMinutes = w.SlotStepMinutes
    };
}
