using Microsoft.EntityFrameworkCore;

namespace AvailabilityServiceApi;

public class BlackoutDateService
{
    private readonly CatalogDbContext _db;

    public BlackoutDateService(CatalogDbContext db) => _db = db;

    /// <summary>Is the date closed for booking in the given hall (or for the whole restaurant)?</summary>
    public async Task<bool> IsBlackoutAsync(DateTime date, Guid? hallId, CancellationToken ct = default)
    {
        var day = date.Date;
        return await _db.BlackoutDates.AnyAsync(
            b => b.Date == day && b.IsFullHall && (b.HallId == null || b.HallId == hallId), ct);
    }

    public async Task<BlackoutDateResponse> AddAsync(BlackoutDateRequest request)
    {
        if (request.HallId is Guid hallId && !await _db.Halls.AnyAsync(h => h.Id == hallId))
            throw new ServiceException(404, "Hall not found");

        var blackout = new BlackoutDate
        {
            Id = Guid.NewGuid(),
            Date = request.Date.Date,
            Reason = request.Reason.Trim(),
            IsFullHall = request.IsFullHall,
            HallId = request.HallId
        };
        _db.BlackoutDates.Add(blackout);
        await _db.SaveChangesAsync();
        return Map(blackout);
    }

    public async Task<List<BlackoutDateResponse>> GetAllAsync()
    {
        var items = await _db.BlackoutDates.AsNoTracking().OrderBy(b => b.Date).ToListAsync();
        return items.Select(Map).ToList();
    }

    private static BlackoutDateResponse Map(BlackoutDate b) => new()
    {
        Id = b.Id,
        Date = b.Date,
        Reason = b.Reason,
        IsFullHall = b.IsFullHall,
        HallId = b.HallId
    };
}
