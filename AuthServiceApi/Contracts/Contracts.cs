using System.ComponentModel.DataAnnotations;

namespace AuthServiceApi;

// ---------- requests ----------

public class RegisterRequest
{
    [Required, EmailAddress, MaxLength(256)] public string Email { get; set; } = "";
    [Required, MinLength(8), MaxLength(128)] public string Password { get; set; } = "";
    [Required, MaxLength(100)] public string FirstName { get; set; } = "";
    [Required, MaxLength(100)] public string LastName { get; set; } = "";
    [Required, MaxLength(20)] public string PhoneNumber { get; set; } = "";
}

public class LoginRequest
{
    [Required] public string Email { get; set; } = "";
    [Required] public string Password { get; set; } = "";
}

public class RefreshTokenRequest
{
    [Required] public string RefreshToken { get; set; } = "";
}

public class ChangePasswordRequest
{
    [Required] public string CurrentPassword { get; set; } = "";
    [Required, MinLength(8), MaxLength(128)] public string NewPassword { get; set; } = "";
}

public class UpdateProfileRequest
{
    [Required, MaxLength(100)] public string FirstName { get; set; } = "";
    [Required, MaxLength(100)] public string LastName { get; set; } = "";
    [Required, MaxLength(20)] public string PhoneNumber { get; set; } = "";
}

// ---------- responses ----------

public class AuthResponse
{
    public Guid UserId { get; set; }
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime AccessTokenExpiresAt { get; set; }
}

public class UserInfoResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string[] Roles { get; set; } = Array.Empty<string>();
    public DateTime CreationDate { get; set; }
}
