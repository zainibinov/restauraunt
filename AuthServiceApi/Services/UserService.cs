using Microsoft.EntityFrameworkCore;

namespace AuthServiceApi;

public class UserService
{
    private readonly AuthDbContext _db;

    public UserService(AuthDbContext db) => _db = db;

    public async Task<UserInfoResponse> GetAsync(Guid userId)
    {
        var user = await _db.Users.AsNoTracking()
                       .Include(u => u.Roles)
                       .FirstOrDefaultAsync(u => u.Id == userId)
                   ?? throw new ServiceException(404, "User not found");
        return Map(user);
    }

    public async Task<UserInfoResponse> UpdateProfileAsync(Guid userId, UpdateProfileRequest request)
    {
        var user = await _db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == userId)
                   ?? throw new ServiceException(404, "User not found");

        var phone = request.PhoneNumber.Trim();
        if (phone != user.PhoneNumber && await _db.Users.AnyAsync(u => u.PhoneNumber == phone))
            throw new ServiceException(409, "This phone number is already used");

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.PhoneNumber = phone;
        await _db.SaveChangesAsync();

        return Map(user);
    }

    /// <summary>Contact data for the gRPC server (null = there is no such user).</summary>
    public async Task<UserContactDto?> GetContactAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.Email, u.PhoneNumber, u.FirstName, u.LastName })
            .FirstOrDefaultAsync(ct);

        return user is null
            ? null
            : new UserContactDto(user.Id, user.Email, user.PhoneNumber, $"{user.FirstName} {user.LastName}".Trim());
    }

    private static UserInfoResponse Map(User user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        FirstName = user.FirstName,
        LastName = user.LastName,
        PhoneNumber = user.PhoneNumber,
        Roles = user.Roles.Select(r => r.Name).ToArray(),
        CreationDate = user.CreationDate
    };
}

public record UserContactDto(Guid UserId, string Email, string Phone, string FullName);
