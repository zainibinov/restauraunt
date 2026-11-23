namespace AuthServiceApi;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public DateTime CreationDate { get; set; }

    public List<Role> Roles { get; set; } = new();
    public List<RefreshToken> RefreshTokens { get; set; } = new();
}

public class Role
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    public List<User> Users { get; set; } = new();
}

public static class RoleNames
{
    public const string Guest = "Guest";
    public const string Admin = "Admin";
    public const string Waiter = "Waiter";

    public static readonly string[] All = { Guest, Admin, Waiter };
}

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime ExpireDate { get; set; }
    public bool IsRevoked { get; set; }

    public User User { get; set; } = null!;
}
