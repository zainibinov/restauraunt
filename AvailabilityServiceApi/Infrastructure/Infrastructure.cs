using Microsoft.AspNetCore.Mvc;

namespace AvailabilityServiceApi;

// ---------- time (can be replaced in tests) ----------

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}

public class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}

// ---------- JWT settings (must match AuthService!) ----------

public class JwtSettings
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string Key { get; set; } = "";
}

public static class RoleNames
{
    public const string Guest = "Guest";
    public const string Admin = "Admin";
    public const string Waiter = "Waiter";
}

public static class TableTypes
{
    public static bool TryParse(string? value, out TableType type) =>
        Enum.TryParse(value, true, out type) && Enum.IsDefined(type);
}

// ---------- occupancy by bookings ----------

public record BookingStart(Guid TableId, DateTime Start);

/// <summary>
/// Where the service learns about existing bookings. The bookings belong to BookingService,
/// so for now this is an empty stub. Later it is replaced by a gRPC client.
/// </summary>
public interface IOccupancyProvider
{
    Task<List<BookingStart>> GetBookingStartsAsync(
        DateTime date, IReadOnlyCollection<Guid> tableIds, CancellationToken ct = default);
}

public class NullOccupancyProvider : IOccupancyProvider
{
    public Task<List<BookingStart>> GetBookingStartsAsync(
        DateTime date, IReadOnlyCollection<Guid> tableIds, CancellationToken ct = default)
        => Task.FromResult(new List<BookingStart>());
}

// ---------- errors ----------

public class ServiceException : Exception
{
    public int StatusCode { get; }
    public ServiceException(int statusCode, string message) : base(message) => StatusCode = statusCode;
}

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ServiceException ex)
        {
            context.Response.StatusCode = ex.StatusCode;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = ex.StatusCode,
                Title = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = 500,
                Title = "Internal Server Error"
            });
        }
    }
}
