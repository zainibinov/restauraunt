using Microsoft.EntityFrameworkCore;

namespace AvailabilityServiceApi;

public class HallService
{
    private readonly CatalogDbContext _db;

    public HallService(CatalogDbContext db) => _db = db;

    public async Task<List<HallResponse>> GetAllAsync()
    {
        var halls = await _db.Halls.AsNoTracking().OrderBy(h => h.Name).ToListAsync();
        return halls.Select(Map).ToList();
    }

    public async Task<HallResponse> CreateAsync(HallRequest request)
    {
        var hall = new Hall
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description?.Trim() ?? ""
        };
        _db.Halls.Add(hall);
        await _db.SaveChangesAsync();
        return Map(hall);
    }

    private static HallResponse Map(Hall h) => new()
    {
        Id = h.Id,
        Name = h.Name,
        Description = h.Description
    };
}
