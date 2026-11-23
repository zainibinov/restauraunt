using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AuthServiceApi;

public class AuthService
{
    private readonly AuthDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly JwtTokenService _jwt;
    private readonly IDateTimeProvider _clock;
    private readonly JwtSettings _settings;

    public AuthService(AuthDbContext db, IPasswordHasher hasher, JwtTokenService jwt,
        IDateTimeProvider clock, IOptions<JwtSettings> settings)
    {
        _db = db;
        _hasher = hasher;
        _jwt = jwt;
        _clock = clock;
        _settings = settings.Value;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var phone = request.PhoneNumber.Trim();

        if (await _db.Users.AnyAsync(u => u.Email == email || u.PhoneNumber == phone))
            throw new ServiceException(409, "A user with this email or phone already exists");

        var guestRole = await _db.Roles.SingleAsync(r => r.Name == RoleNames.Guest);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PhoneNumber = phone,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PasswordHash = _hasher.Hash(request.Password),
            CreationDate = _clock.UtcNow,
            Roles = { guestRole }
        };
        _db.Users.Add(user);

        var response = IssueTokens(user);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // two simultaneous registrations: the unique index is the last line of defence
            throw new ServiceException(409, "A user with this email or phone already exists");
        }
        return response;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Email == email);

        // same message for "no such user" and "wrong password" - do not reveal which one
        if (user is null || !_hasher.Verify(request.Password, user.PasswordHash))
            throw new ServiceException(401, "Invalid email or password");

        var response = IssueTokens(user);
        await _db.SaveChangesAsync();
        return response;
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken)
    {
        var hash = JwtTokenService.HashToken(refreshToken);
        var token = await _db.RefreshTokens
            .Include(t => t.User).ThenInclude(u => u.Roles)
            .FirstOrDefaultAsync(t => t.TokenHash == hash);

        if (token is null || token.IsRevoked || token.ExpireDate <= _clock.UtcNow)
            throw new ServiceException(401, "Refresh token is invalid, expired or revoked");

        token.IsRevoked = true; // the old token is single-use
        var response = IssueTokens(token.User);
        await _db.SaveChangesAsync();
        return response;
    }

    public async Task LogoutAsync(Guid userId, string refreshToken)
    {
        var hash = JwtTokenService.HashToken(refreshToken);
        var token = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.UserId == userId);

        if (token is not null)
        {
            token.IsRevoked = true;
            await _db.SaveChangesAsync();
        }
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId)
                   ?? throw new ServiceException(404, "User not found");

        if (!_hasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new ServiceException(400, "Current password is incorrect");

        user.PasswordHash = _hasher.Hash(request.NewPassword);
        await _db.SaveChangesAsync();

        // log the user out everywhere
        await _db.RefreshTokens
            .Where(t => t.UserId == userId && !t.IsRevoked)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsRevoked, true));
    }

    /// <summary>Called by the background cleanup worker (you write it later).</summary>
    public async Task<int> DeleteExpiredRefreshTokensAsync()
    {
        var now = _clock.UtcNow;
        return await _db.RefreshTokens.Where(t => t.ExpireDate < now).ExecuteDeleteAsync();
    }

    // Creates the token pair and adds the refresh token to the context
    // (SaveChanges is called by the caller).
    private AuthResponse IssueTokens(User user)
    {
        var (accessToken, expiresAt) = _jwt.CreateAccessToken(user);
        var refreshToken = _jwt.GenerateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = JwtTokenService.HashToken(refreshToken),
            ExpireDate = _clock.UtcNow.AddDays(_settings.RefreshTokenDays),
            IsRevoked = false
        });

        return new AuthResponse
        {
            UserId = user.Id,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiresAt = expiresAt
        };
    }
}
