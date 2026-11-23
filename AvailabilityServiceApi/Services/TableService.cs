using Microsoft.EntityFrameworkCore;

namespace AvailabilityServiceApi;

public class TableService
{
    private readonly CatalogDbContext _db;

    public TableService(CatalogDbContext db) => _db = db;

    public async Task<List<TableResponse>> GetByHallAsync(Guid hallId)
    {
        if (!await _db.Halls.AnyAsync(h => h.Id == hallId))
            throw new ServiceException(404, "Hall not found");

        var tables = await _db.Tables.AsNoTracking()
            .Where(t => t.HallId == hallId)
            .OrderBy(t => t.TableNumber)
            .ToListAsync();
        return tables.Select(Map).ToList();
    }

    public async Task<TableResponse> CreateAsync(Guid hallId, TableRequest request)
    {
        if (!await _db.Halls.AnyAsync(h => h.Id == hallId))
            throw new ServiceException(404, "Hall not found");

        if (!TableTypes.TryParse(request.TableType, out var type))
            throw new ServiceException(400, "Unknown table type. Allowed: Standard, Vip, Bar");

        if (await _db.Tables.AnyAsync(t => t.HallId == hallId && t.TableNumber == request.TableNumber))
            throw new ServiceException(409, "A table with this number already exists in the hall");

        var table = new RestaurantTable
        {
            Id = Guid.NewGuid(),
            HallId = hallId,
            TableNumber = request.TableNumber,
            Capacity = request.Capacity,
            TableType = type
        };
        _db.Tables.Add(table);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // two simultaneous requests: the unique index (hall_id, table_number) is the last line of defence
            throw new ServiceException(409, "A table with this number already exists in the hall");
        }
        return Map(table);
    }

    private static TableResponse Map(RestaurantTable t) => new()
    {
        Id = t.Id,
        TableNumber = t.TableNumber,
        Capacity = t.Capacity,
        TableType = t.TableType.ToString()
    };
}
