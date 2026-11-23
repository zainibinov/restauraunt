using Microsoft.EntityFrameworkCore;

namespace AuthServiceApi;

/// <summary>On startup: creates the schema (if missing) and seeds the roles.</summary>
public class AuthDbInitializer : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public AuthDbInitializer(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        await db.Database.EnsureCreatedAsync(cancellationToken);

        foreach (var name in RoleNames.All)
        {
            if (!await db.Roles.AnyAsync(r => r.Name == name, cancellationToken))
                db.Roles.Add(new Role { Name = name });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
