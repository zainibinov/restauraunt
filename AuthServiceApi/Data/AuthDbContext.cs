using Microsoft.EntityFrameworkCore;

namespace AuthServiceApi;

public class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("user");
            e.HasIndex(u => u.Email).IsUnique();
            e.HasIndex(u => u.PhoneNumber).IsUnique();
            e.Property(u => u.Email).HasMaxLength(256);
            e.Property(u => u.PhoneNumber).HasMaxLength(20);
            e.Property(u => u.PasswordHash).HasMaxLength(512);
            e.Property(u => u.FirstName).HasMaxLength(100);
            e.Property(u => u.LastName).HasMaxLength(100);

            // many-to-many: table user_role(user_id, role_id) from the diagram
            e.HasMany(u => u.Roles).WithMany(r => r.Users)
                .UsingEntity<Dictionary<string, object>>(
                    "user_role",
                    right => right.HasOne<Role>().WithMany().HasForeignKey("role_id"),
                    left => left.HasOne<User>().WithMany().HasForeignKey("user_id"));
        });

        b.Entity<Role>(e =>
        {
            e.ToTable("role");
            e.HasIndex(r => r.Name).IsUnique();
            e.Property(r => r.Name).HasMaxLength(50);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_token");
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.Property(t => t.TokenHash).HasMaxLength(512);
            e.HasOne(t => t.User).WithMany(u => u.RefreshTokens).HasForeignKey(t => t.UserId);
        });
    }
}
